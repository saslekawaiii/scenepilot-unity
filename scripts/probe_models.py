"""Read external credentials without printing them; save only sanitized evidence."""
import argparse
import json
import time
import urllib.error
import urllib.request
import ssl
import certifi
from datetime import datetime, timezone
from pathlib import Path


def read_env(path):
    result = {}
    for line in Path(path).read_text().splitlines():
        line = line.strip()
        if not line or line.startswith('#') or '=' not in line:
            continue
        key, value = line.removeprefix('export ').split('=', 1)
        result[key.strip()] = value.strip().strip('\"\'')
    return result


def request(url, key='', payload=None, timeout=90):
    headers = {'Content-Type': 'application/json', 'User-Agent': 'WorldWeaver/1.0'}
    if key:
        headers['Authorization'] = 'Bearer ' + key
    req = urllib.request.Request(url, data=None if payload is None else json.dumps(payload).encode(), headers=headers)
    started = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=timeout, context=ssl.create_default_context(cafile=certifi.where())) as response:
            return response.status, json.load(response), time.perf_counter() - started
    except urllib.error.HTTPError as error:
        # Provider errors may echo part of a key. Never store their raw text.
        return error.code, {}, time.perf_counter() - started
    except Exception as error:
        return type(error).__name__, {}, time.perf_counter() - started


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--env', required=True)
    args = parser.parse_args()
    env = read_env(args.env)
    rows = []
    for provider, endpoint, variable, models in [
        ('groq', 'https://api.groq.com/openai/v1', 'GROQ_API_KEY', [env.get('LLM_MODEL', 'openai/gpt-oss-120b'), 'openai/gpt-oss-20b']),
        ('openai', 'https://api.openai.com/v1', 'OPENAI_API_KEY', ['gpt-4.1-mini', 'gpt-4.1-nano'])
    ]:
        if not env.get(variable):
            continue
        status, data, seconds = request(endpoint + '/models', env[variable])
        ids = {m['id'] for m in data.get('data', [])}
        rows.append({'provider': provider, 'operation': 'models', 'status': status, 'seconds': seconds,
                     'model_count': len(ids), 'configured_model_listed': env.get('LLM_MODEL') in ids})
        for model in models:
            payload = {'model': model, 'messages': [{'role': 'user', 'content': 'Return JSON only: {"ready":true}'}],
                       'temperature': 0, 'max_completion_tokens': 128, 'response_format': {'type': 'json_object'}}
            status, data, seconds = request(endpoint + '/chat/completions', env[variable], payload)
            rows.append({'provider': provider, 'operation': 'chat', 'model': model, 'status': status, 'seconds': seconds,
                         'content': data.get('choices', [{}])[0].get('message', {}).get('content'), 'usage': data.get('usage')})
    out = Path('results/provider-probe.json')
    out.parent.mkdir(exist_ok=True)
    out.write_text(json.dumps({'timestamp': datetime.now(timezone.utc).isoformat(), 'results': rows}, ensure_ascii=False, indent=2))
    print(json.dumps(rows, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
