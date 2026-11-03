import array
import base64
import json
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch
from PIL import Image
import bridge
from ollama_model import OllamaToolModel


class BridgeContracts(unittest.TestCase):
    def test_data_index_preserves_alternatives_outside_the_last_large_tile_group(self):
        sources = {"https://example.com/small/global.img": {"observedUtc": 1}}
        sources.update({f"https://example.com/large/tile{i}.img": {"observedUtc": i + 2} for i in range(100)})
        variants = bridge.data_variants(sources)
        self.assertEqual(24, len(variants)); self.assertIn("https://example.com/small/global.img", [v["url"] for v in variants])

    def test_public_network_boundaries(self):
        for value in ["file:///etc/passwd", "http://example.com", "https://localhost/x", "https://127.0.0.1/x", "https://[::1]/x", "https://169.254.169.254/latest/meta-data", "https://user:pass@example.com", "https://example.com:11434/"]:
            with self.subTest(url=value), self.assertRaises(ValueError):
                bridge.public_url(value)
        with patch("socket.getaddrinfo", return_value=[(None, None, None, None, ("10.0.0.1", 443))]), self.assertRaises(ValueError):
            bridge.public_url("https://apparently-public.example/x")

    def test_unobserved_urls_cannot_be_downloaded(self):
        with tempfile.TemporaryDirectory() as directory:
            worker = bridge.Research(Path(directory) / "sources.json")
            with patch("socket.getaddrinfo", return_value=[(None, None, None, None, ("93.184.216.34", 443))]), self.assertRaisesRegex(ValueError, "not observed"):
                worker.download("https://example.com/imagined.img")

    def test_pds_signed_endian_and_actual_source_hash(self):
        with tempfile.TemporaryDirectory() as directory:
            data, label = Path(directory) / "height.img", Path(directory) / "height.lbl"
            data.write_bytes(struct.pack(">6h", -8000, -1000, 2000, 3000, 9000, 21000))
            label.write_text("PDS_VERSION_ID = PDS3\nLINES = 2\nLINE_SAMPLES = 3\nSAMPLE_BITS = 16\nSAMPLE_TYPE = MSB_INTEGER\n")
            result = bridge.decode_heightmap({"path": str(data), "label_path": str(label), "format": "pds3", "resolution": 33})
            self.assertEqual((-8000, 21000), (result["sampleMin"], result["sampleMax"]))
            self.assertEqual((3, 2), (result["sourceWidth"], result["sourceHeight"]))
            values = struct.unpack("<1089f", base64.b64decode(result["normalizedFloat32LE"]))
            self.assertEqual(0, values[0]); self.assertEqual(1, values[-1])
            data.write_bytes(data.read_bytes()[:-1])
            with self.assertRaisesRegex(ValueError, "exact source byte count"):
                bridge.decode_heightmap({"path": str(data), "label_path": str(label), "format": "pds3"})

    def test_colored_relief_is_not_an_elevation_map(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "color.png"
            Image.new("RGB", (4, 4), (128, 30, 8)).save(path)
            with self.assertRaisesRegex(ValueError, "not a color relief"):
                bridge.decode_heightmap({"path": str(path)})

    def test_script_download_is_rejected_even_if_page_linked_it(self):
        with tempfile.TemporaryDirectory() as directory:
            worker = bridge.Research(Path(directory) / "sources.json")
            worker.remember("https://example.com/Run.cs", "https://example.com/")
            with patch("bridge.public_url", side_effect=lambda value, **kwargs: value), patch("bridge.fetch", return_value=("https://example.com/Run.cs", b"class Run{}", "text/plain")), self.assertRaisesRegex(ValueError, "Unsupported data file type"):
                worker.download("https://example.com/Run.cs")

    def test_observed_detached_pds_label_is_downloaded_with_its_image(self):
        with tempfile.TemporaryDirectory() as directory:
            worker = bridge.Research(Path(directory) / "sources.json")
            image, label = "https://example.com/data/height.img", "https://example.com/data/height.lbl"
            worker.remember(image, "https://example.com/data/"); worker.remember(label, "https://example.com/data/")
            metadata = b'PDS_VERSION_ID = PDS3\n^IMAGE = "HEIGHT.IMG"\n'
            with patch("bridge.public_url", side_effect=lambda value, **kwargs: value), patch("bridge.fetch", side_effect=[(image, b"\0\1", "application/octet-stream"), (label, metadata, "text/plain")]):
                result = worker.download(image)
            self.assertEqual(1, len(result["companions"]))
            self.assertEqual(metadata, base64.b64decode(result["companions"][0]["data"]))

    def test_unicode_heightmap_path_survives_ascii_json_wire(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "Карта высот.png"
            image = Image.new("L", (4, 4), 40); image.putpixel((3, 3), 200); image.save(path)
            request = json.loads(json.dumps({"path": str(path), "resolution": 33}, ensure_ascii=True))
            self.assertEqual(4, bridge.decode_heightmap(request)["sourceWidth"])

    def test_native_model_preserves_structured_calls_and_omits_text_stop_markers(self):
        model = OllamaToolModel("test:cloud")
        with patch.object(model.session, "post") as post:
            post.return_value.json.return_value = {"message": {"content": "", "tool_calls": [{"function": {"name": "web_read", "arguments": {"url": "https://example.com/"}}}]}, "prompt_eval_count": 7, "eval_count": 3}
            response = model.generate([{"role": "user", "content": [{"type": "text", "text": "Find a source"}]}], stop_sequences=["Calling tools:", "Observation:"])
        body = post.call_args.kwargs["json"]
        self.assertNotIn("stop", body); self.assertNotIn("stop", body["options"])
        self.assertNotIn("think", body, "Cloud research must not forcibly disable native reasoning")
        self.assertEqual("web_read", response.tool_calls[0].function.name)
        self.assertEqual({"url": "https://example.com/"}, response.tool_calls[0].function.arguments)


if __name__ == "__main__":
    unittest.main()
