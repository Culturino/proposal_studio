#!/bin/bash
# Run on the Host box. Reads RDS settings from app.env and never prints the password.
set -euo pipefail
python3 << 'PY'
import os, subprocess, pathlib
env = {}
for line in open("/etc/proposal-studio/app.env"):
    line = line.strip()
    if not line or line.startswith("#") or "=" not in line:
        continue
    k, v = line.split("=", 1)
    env[k] = v
parts = dict(p.split("=", 1) for p in env["ConnectionStrings__DefaultConnection"].split(";") if "=" in p)
os.environ["PGPASSWORD"] = parts["Password"]
sql_path = "/tmp/scrub_joke_data.sql"
out = subprocess.check_output([
    "psql", "-h", parts["Host"], "-p", parts.get("Port", "5432"),
    "-U", parts["Username"], "-d", parts["Database"],
    "-v", "ON_ERROR_STOP=1", "-f", sql_path,
], text=True)
print(out.strip())
print("SCRUB_OK")
pathlib.Path(sql_path).unlink(missing_ok=True)
PY
