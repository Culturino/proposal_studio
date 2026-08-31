#!/bin/bash
set -euo pipefail
dnf install -y httpd-tools >/dev/null
export NEW_HASH
NEW_HASH=$(htpasswd -nbBC 10 x 'Password123!' | cut -d: -f2)
python3 << 'PY'
import os, subprocess
env = {}
for line in open("/etc/proposal-studio/app.env"):
    line = line.strip()
    if not line or line.startswith("#") or "=" not in line:
        continue
    k, v = line.split("=", 1)
    env[k] = v
parts = dict(p.split("=", 1) for p in env["ConnectionStrings__DefaultConnection"].split(";") if "=" in p)
os.environ["PGPASSWORD"] = parts["Password"]
escaped = os.environ["NEW_HASH"].replace("'", "''")
sql = (
    "UPDATE users SET password_hash = '{h}', active = true, two_factor_enabled = false "
    "WHERE lower(email) IN ('admin@houseofpianos.ae', 'shavkat@example.com'); "
    "SELECT email FROM users WHERE lower(email) IN ('admin@houseofpianos.ae', 'shavkat@example.com') "
    "AND active ORDER BY email;"
).format(h=escaped)
out = subprocess.check_output([
    "psql", "-h", parts["Host"], "-p", parts.get("Port", "5432"),
    "-U", parts["Username"], "-d", parts["Database"],
    "-v", "ON_ERROR_STOP=1", "-At", "-c", sql,
], text=True)
print(out.strip())
print("PASSWORD_RESET_OK")
PY
