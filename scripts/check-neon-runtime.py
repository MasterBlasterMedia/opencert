"""Verify an isolated Neon database with the real Production API and key ring.

Requires built Debug API binaries, OpenSSL, and a private file containing a Neon
Postgres URI for a database ending in _ci or _tests. No email or CA calls occur.
The wrapping key is retained in a private ignored directory for repeat runs.
"""
import argparse
import http.client
import json
import os
from pathlib import Path
import secrets
import socket
import stat
import subprocess
import time
from urllib.parse import unquote, urlparse

ROOT = Path(__file__).resolve().parent.parent
PORT = 5082
PUBLIC_HOST = 'runtime-check.example.invalid'


def private_write(path, value):
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, 'w') as output:
        output.write(value)


def connection(uri, pooled):
    parsed = urlparse(uri)
    name = unquote(parsed.path.removeprefix('/'))
    if (parsed.scheme not in ('postgres', 'postgresql') or not parsed.hostname
            or not parsed.hostname.endswith('.neon.tech')
            or not name.endswith(('_ci', '_tests')) or not parsed.username or not parsed.password):
        raise ValueError('Supply a Neon connection URI for an isolated _ci or _tests database.')
    host = parsed.hostname.replace('-pooler.', '.')
    if pooled:
        first, rest = host.split('.', 1)
        host = first + '-pooler.' + rest
    def quote(value):
        return '"' + str(value).replace('"', '""') + '"'
    fields = dict(Host=host, Port=parsed.port or 5432, Database=name,
                  Username=unquote(parsed.username), Password=unquote(parsed.password))
    return ';'.join(key + '=' + quote(value) for key, value in fields.items()) + ';SSL Mode=VerifyFull;Timeout=30;Maximum Pool Size=5'


def request(path, method='GET', body=None, token=None, cookie=None):
    client = http.client.HTTPConnection('127.0.0.1', PORT, timeout=40)
    headers = {'Host': PUBLIC_HOST, 'X-Forwarded-Proto': 'https', 'Content-Type': 'application/json'}
    if token:
        headers['X-CSRF-Token'] = token
    if cookie:
        headers['Cookie'] = cookie
    try:
        client.request(method, path, json.dumps(body) if body is not None else None, headers)
        response = client.getresponse()
        status, values, payload = response.status, response.getheaders(), response.read()
        return status, values, json.loads(payload) if payload else None
    finally:
        client.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--connection-file', required=True, type=Path)
    args = parser.parse_args()
    if args.connection_file.is_symlink() or stat.S_IMODE(args.connection_file.stat().st_mode) & 0o077:
        raise ValueError('The connection file must be private (0600) and not a symlink.')
    uri = args.connection_file.read_text().strip()
    direct, pooled = connection(uri, False), connection(uri, True)
    os.chdir(ROOT)
    with socket.socket() as check:
        if check.connect_ex(('127.0.0.1', PORT)) == 0:
            raise RuntimeError('The local runtime-check port is already occupied.')
    directory = ROOT / '.local/runtime-check'
    if directory.is_symlink():
        raise ValueError('The runtime-check directory must not be a symlink.')
    directory.mkdir(mode=0o700, parents=True, exist_ok=True)
    os.chmod(directory, 0o700)
    password = directory / 'wrapping-password'
    certificate = directory / 'wrapping.pfx'
    if not certificate.exists():
        if password.exists():
            raise RuntimeError('An incomplete wrapping-key setup exists; inspect the private runtime-check directory.')
        private_write(password, secrets.token_urlsafe(48))
        key, public = directory / 'wrapping-key.pem', directory / 'wrapping-public.pem'
        previous_mask = os.umask(0o077)
        try:
            subprocess.run(['openssl', 'req', '-x509', '-newkey', 'rsa:3072', '-noenc', '-days', '30',
                            '-subj', '/CN=OpenCert isolated runtime check', '-keyout', str(key), '-out', str(public)],
                           check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            subprocess.run(['openssl', 'pkcs12', '-export', '-inkey', str(key), '-in', str(public),
                            '-out', str(certificate), '-passout', 'file:' + str(password)],
                           check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        finally:
            os.umask(previous_mask)
            key.unlink(missing_ok=True)
            public.unlink(missing_ok=True)
    for secret in (certificate, password):
        if secret.is_symlink() or stat.S_IMODE(secret.stat().st_mode) & 0o077:
            raise ValueError('Wrapping material must be private and not symlinked.')
    dotnet = os.environ.get('OPENCERT_DOTNET', 'dotnet')
    command = [dotnet, 'apps/api/bin/Debug/net10.0/OpenCert.Api.dll']
    env = dict(os.environ, ASPNETCORE_ENVIRONMENT='Production', DOTNET_ENVIRONMENT='Production',
               ConnectionStrings__Database=direct, Web__Origin='https://' + PUBLIC_HOST,
               ASPNETCORE_URLS=f'http://127.0.0.1:{PORT}', Security__TrustedProxies='127.0.0.1',
               Security__TrustedProxyNetworks='', Security__KeyEncryptionCertificate=str(certificate),
               Security__KeyEncryptionCertificateBase64='', Security__KeyEncryptionPassword=password.read_text(),
               Email__ResendApiKey='', Logging__LogLevel__Default='Warning')
    # Disable any inherited shortcut for proxy trust; this check uses exactly one loopback proxy.
    env.pop('ASPNETCORE_FORWARDEDHEADERS_ENABLED', None)
    token = cookie = None
    with (directory / 'runtime.log').open('w') as log:
        os.chmod(directory / 'runtime.log', 0o600)
        subprocess.run(command + ['--migrate'], env=env, stdout=log, stderr=log, check=True, timeout=120)
        for name, database in [('direct', direct), ('pooled after restart', pooled)]:
            env['ConnectionStrings__Database'] = database
            process = subprocess.Popen(command, env=env, stdout=log, stderr=log)
            try:
                for _ in range(60):
                    if process.poll() is not None:
                        raise RuntimeError('Runtime API exited; inspect the private runtime.log.')
                    try:
                        if request('/health/ready')[0] == 200:
                            break
                    except OSError:
                        pass
                    time.sleep(1)
                else:
                    raise RuntimeError('Runtime API did not become ready.')
                if token is None:
                    status, headers, body = request('/api/v1/auth/csrf')
                    assert status == 200, 'Direct API failed to create an antiforgery token'
                    token = body['token']
                    cookie_header = next(value for key, value in headers if key.lower() == 'set-cookie' and value.startswith('oc.csrf='))
                    assert 'secure' in cookie_header.lower() and 'httponly' in cookie_header.lower()
                    cookie = cookie_header.split(';', 1)[0]
                body = {'flowId': '00000000-0000-0000-0000-000000000000', 'code': '00000000'}
                status, _, result = request('/api/v1/auth/email/finish', 'POST', body, token, cookie)
                assert status == 400 and result['title'] == 'This code expired or belongs to another browser. Request a new code.', 'Persisted antiforgery token failed validation'
                status, _, result = request('/api/v1/auth/email/finish', 'POST', body, 'invalid-token', cookie)
                assert status == 400 and result['title'] == 'Your page security token expired. Refresh and try again.', 'Invalid antiforgery token was not rejected'
                print(f'Passed Production API, VerifyFull database connection and CSRF checks: {name}', flush=True)
            finally:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
    print('Persisted encrypted key-ring restart check passed; no email or CA request was made.')


if __name__ == '__main__':
    main()
