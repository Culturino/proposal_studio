#!/bin/bash
set -euo pipefail
BUCKET="proposal-studio-media-378356708213"
REGION="eu-central-1"
SECRET_ARN="arn:aws:secretsmanager:eu-central-1:378356708213:secret:rds!db-51d933d7-6529-470b-bc9f-d0876a0d2d5d-QH1uxz"
RDS_HOST="$1"
PUBLIC_HOST="$2"

dnf install -y unzip tar python3 libicu fontconfig
if ! command -v pg_restore >/dev/null 2>&1; then
  dnf install -y postgresql18 || dnf install -y postgresql17 || dnf install -y postgresql16
fi
if [ ! -x /usr/local/bin/caddy ]; then
  curl -sL "https://caddyserver.com/api/download?os=linux&arch=amd64" -o /usr/local/bin/caddy
  chmod +x /usr/local/bin/caddy
fi

mkdir -p /opt/proposal-studio /etc/proposal-studio /var/log/proposal-studio
aws s3 cp "s3://${BUCKET}/releases/publish.tar.gz" /tmp/publish.tar.gz --region "$REGION"
rm -rf /opt/proposal-studio
mkdir -p /opt/proposal-studio
tar -xzf /tmp/publish.tar.gz -C /opt/proposal-studio
chmod +x /opt/proposal-studio/ProposalStudio

JWT=$(python3 -c "import secrets; print(secrets.token_urlsafe(48))")
python3 - "$SECRET_ARN" "$RDS_HOST" "$JWT" <<'PY'
import json, os, subprocess, sys
secret_arn, host, jwt = sys.argv[1], sys.argv[2], sys.argv[3]
raw = subprocess.check_output([
    "aws", "secretsmanager", "get-secret-value",
    "--secret-id", secret_arn, "--query", "SecretString", "--output", "text",
    "--region", "eu-central-1"
], text=True)
data = json.loads(raw)
user = data.get("username", "hopstudio")
password = data["password"].replace("'", "''")
path = "/etc/proposal-studio/app.env"
with open(path, "w") as f:
    f.write(f"ASPNETCORE_ENVIRONMENT=Host\n")
    f.write(f"ASPNETCORE_URLS=http://127.0.0.1:5080\n")
    f.write(f"ConnectionStrings__DefaultConnection=Host={host};Port=5432;Database=proposal_studio;Username={user};Password={password};SSL Mode=Require;Trust Server Certificate=true\n")
    f.write(f"JwtSettings__SecretKey={jwt}\n")
    f.write("Seed__DemoUsers=false\n")
    f.write("Seed__SeedCatalog=false\n")
    f.write("Storage__Bucket=proposal-studio-media-378356708213\n")
    f.write("Storage__Region=eu-central-1\n")
os.chmod(path, 0o600)
PY

if aws s3 ls "s3://${BUCKET}/releases/db.dump" --region "$REGION" >/dev/null 2>&1; then
  aws s3 cp "s3://${BUCKET}/releases/db.dump" /tmp/db.dump --region "$REGION"
  set +o allexport
  # shellcheck disable=SC1091
  DBPASS=$(python3 - "$SECRET_ARN" <<'PY'
import json, subprocess, sys
raw = subprocess.check_output([
    "aws", "secretsmanager", "get-secret-value",
    "--secret-id", sys.argv[1], "--query", "SecretString", "--output", "text",
    "--region", "eu-central-1"
], text=True)
print(json.loads(raw)["password"], end="")
PY
)
  export PGPASSWORD="$DBPASS"
  pg_restore -h "$RDS_HOST" -p 5432 -U hopstudio -d proposal_studio --no-owner --no-acl --clean --if-exists /tmp/db.dump || true
  unset PGPASSWORD
  rm -f /tmp/db.dump
fi

cat >/etc/systemd/system/proposal-studio.service <<'UNIT'
[Unit]
Description=Proposal Studio
After=network.target

[Service]
WorkingDirectory=/opt/proposal-studio
EnvironmentFile=/etc/proposal-studio/app.env
ExecStart=/opt/proposal-studio/ProposalStudio
Restart=always
RestartSec=5
LimitNOFILE=65535

[Install]
WantedBy=multi-user.target
UNIT

mkdir -p /etc/caddy
cat >/etc/caddy/Caddyfile <<EOF
${PUBLIC_HOST} {
  request_body {
    max_size 100MB
  }
  reverse_proxy 127.0.0.1:5080
}
EOF
cat >/etc/systemd/system/caddy.service <<'UNIT'
[Unit]
Description=Caddy
After=network.target

[Service]
ExecStart=/usr/local/bin/caddy run --config /etc/caddy/Caddyfile --adapter caddyfile
Restart=always
AmbientCapabilities=CAP_NET_BIND_SERVICE

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
systemctl enable --now proposal-studio
systemctl enable --now caddy
systemctl is-active proposal-studio
systemctl is-active caddy
