#!/usr/bin/env bash
# Starts/stops the throwaway OpenLDAP server ActiveDirectoryLdapIntegrationTests
# (updatewatch2-server#13) runs against — real docker CLI calls, deliberately
# not a .NET Docker-client library (Testcontainers pulls in SSH.NET, which
# carries a known high-severity CVE, GHSA-q939-rpr3-3284, purely for an
# SSH-exec feature this project would never use — not worth adding to the
# dependency tree of every vulnerability scan of this repo for that). Used
# both by CI (see .github/workflows/docker-publish.yml's ldap-integration-test
# job) and by hand for local development — same script either way, so there
# is exactly one place this container's setup is defined.
#
# Usage: scripts/run-ldap-test-server.sh up|down
set -euo pipefail

CONTAINER_NAME="updatewatch2-ldap-test"
LDAP_PORT="${UPDATEWATCH2_TEST_LDAP_PORT:-389}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SEED_DIR="$SCRIPT_DIR/../tests/UpdateWatch2.Server.Tests/Auth/ldap-seed"

case "${1:-}" in
  up)
    docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true

    # docker cp into a stopped container, not a bind mount: this image's
    # own entrypoint chowns the bootstrap-ldif directory on start and then
    # removes it again once loaded — both steps fail outright against a
    # bind-mounted host directory ("Read-only file system"/"Device or
    # resource busy", found by actually running this, not by reading the
    # image's docs). Copying the file in first makes it a plain file in
    # the container's own writable layer, which the entrypoint can chown
    # and later remove without any host filesystem involved at all.
    docker create \
      --name "$CONTAINER_NAME" \
      -p "${LDAP_PORT}:389" \
      -e LDAP_ORGANISATION="UpdateWatch2 Test" \
      -e LDAP_DOMAIN="example.org" \
      -e LDAP_ADMIN_PASSWORD="admin-p@ssw0rd" \
      -e LDAP_TLS="false" \
      osixia/openldap:1.5.0 >/dev/null

    docker cp "$SEED_DIR/bootstrap.ldif" "$CONTAINER_NAME:/container/service/slapd/assets/config/bootstrap/ldif/custom/bootstrap.ldif"
    docker start "$CONTAINER_NAME" >/dev/null

    echo "Waiting for OpenLDAP to accept connections on port ${LDAP_PORT}..."
    for _ in $(seq 1 30); do
      if (exec 3<>"/dev/tcp/127.0.0.1/${LDAP_PORT}") 2>/dev/null; then
        exec 3>&-
        echo "OpenLDAP is up."
        exit 0
      fi
      sleep 1
    done

    echo "OpenLDAP never opened port ${LDAP_PORT} within 30s — see 'docker logs ${CONTAINER_NAME}'." >&2
    exit 1
    ;;
  down)
    docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
    ;;
  *)
    echo "Usage: $0 up|down" >&2
    exit 1
    ;;
esac
