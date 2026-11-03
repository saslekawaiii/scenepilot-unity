"""Install the optional open-source worker into the current user's application data.

Run with Python 3.12. No administrator access, API purchase or browser account.
"""
from pathlib import Path
import subprocess
import sys

root = Path.home() / ".local/share/worldweaver/web-agent/venv"
python = root / ("Scripts/python.exe" if sys.platform == "win32" else "bin/python")
if not python.exists():
    subprocess.run([sys.executable, "-m", "venv", str(root)], check=True)
subprocess.run([str(python), "-m", "pip", "install", "-r", str(Path(__file__).with_name("requirements.lock"))], check=True)
print("WorldWeaver web worker installed:", python)
print("Search/read use no API key. web_research uses your local Ollama endpoint.")
