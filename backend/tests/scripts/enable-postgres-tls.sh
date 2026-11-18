#!/usr/bin/env bash
set -euo pipefail

tls_dir="${RUNNER_TEMP:?RUNNER_TEMP is required}/mg-postgres-tls"
mkdir -p "$tls_dir"

openssl req -x509 -newkey rsa:2048 -sha256 -nodes -days 2 \
  -keyout "$tls_dir/ca.key" -out "$tls_dir/ca.crt" \
  -subj "/CN=MG CI PostgreSQL Test CA" \
  -addext "basicConstraints=critical,CA:TRUE" \
  -addext "keyUsage=critical,keyCertSign,cRLSign"

openssl req -newkey rsa:2048 -sha256 -nodes \
  -keyout "$tls_dir/server.key" -out "$tls_dir/server.csr" \
  -subj "/CN=localhost"

cat > "$tls_dir/server.ext" <<'EOF'
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:localhost,IP:127.0.0.1
EOF

openssl x509 -req -sha256 -days 2 \
  -in "$tls_dir/server.csr" \
  -CA "$tls_dir/ca.crt" -CAkey "$tls_dir/ca.key" -CAcreateserial \
  -out "$tls_dir/server.crt" -extfile "$tls_dir/server.ext"

container_id="$(docker ps --filter "ancestor=postgres:17" --format '{{.ID}}' | head -n 1)"
if [[ -z "$container_id" ]]; then
  echo "PostgreSQL 17 service container was not found." >&2
  exit 1
fi

docker cp "$tls_dir/server.crt" "$container_id:/var/lib/postgresql/server.crt"
docker cp "$tls_dir/server.key" "$container_id:/var/lib/postgresql/server.key"
docker exec -u root "$container_id" chown postgres:postgres /var/lib/postgresql/server.crt /var/lib/postgresql/server.key
docker exec -u root "$container_id" chmod 644 /var/lib/postgresql/server.crt
docker exec -u root "$container_id" chmod 600 /var/lib/postgresql/server.key

docker exec "$container_id" psql -U postgres -d mg_platform_tests -v ON_ERROR_STOP=1 \
  -c "ALTER SYSTEM SET ssl = 'on';" \
  -c "ALTER SYSTEM SET ssl_cert_file = '/var/lib/postgresql/server.crt';" \
  -c "ALTER SYSTEM SET ssl_key_file = '/var/lib/postgresql/server.key';"

docker restart "$container_id" >/dev/null
ready=false
for attempt in $(seq 1 30); do
  if docker exec "$container_id" pg_isready -U postgres -d mg_platform_tests >/dev/null 2>&1; then
    ready=true
    break
  fi
  sleep 2
done

if [[ "$ready" != "true" ]]; then
  echo "PostgreSQL did not become ready after TLS configuration." >&2
  docker logs "$container_id" >&2
  exit 1
fi

echo "MG_TEST_PG_ROOT_CERT=$tls_dir/ca.crt" >> "$GITHUB_ENV"
