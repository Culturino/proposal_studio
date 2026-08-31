#!/bin/bash
set -euo pipefail
dnf remove -y postgresql16 postgresql16-private-libs postgresql17 postgresql17-private-libs || true
dnf install -y postgresql18
pg_restore --version
aws s3 cp s3://proposal-studio-media-378356708213/releases/db.dump /tmp/db.dump --region eu-central-1
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
base = [
    "psql", "-h", parts["Host"], "-p", parts.get("Port", "5432"),
    "-U", parts["Username"], "-d", parts["Database"], "-v", "ON_ERROR_STOP=1",
]
# Drop app tables so restore is clean. Keep the empty public schema.
subprocess.check_call(base + ["-c", "DROP SCHEMA public CASCADE; CREATE SCHEMA public; GRANT ALL ON SCHEMA public TO PUBLIC;"])
cmd = [
    "pg_restore", "-h", parts["Host"], "-p", parts.get("Port", "5432"),
    "-U", parts["Username"], "-d", parts["Database"],
    "--no-owner", "--no-acl", "--clean", "--if-exists", "/tmp/db.dump",
]
# pg_restore returns 1 on some notices; fail only if tables stay empty.
rc = subprocess.call(cmd)
users = subprocess.check_output(base + ["-tAc", "select count(*) from users"], text=True).strip()
products = subprocess.check_output(base + ["-tAc", "select count(*) from products"], text=True).strip()
print(f"RESTORE_RC {rc}")
print(f"users {users}")
print(f"products {products}")
if int(users) < 1 or int(products) < 1:
    raise SystemExit("restore left users or products empty")
PY
rm -f /tmp/db.dump
systemctl restart proposal-studio
sleep 6
systemctl is-active proposal-studio
ss -lntp | grep 5080 || true
