FROM docker.io/library/postgres:16-alpine

ENV POSTGRES_DB=keycloak \
    POSTGRES_USER=keycloak \
    POSTGRES_PASSWORD=1234 \
    PGDATA=/var/lib/postgresql/data/pgdata

EXPOSE 5432

HEALTHCHECK --interval=5s --timeout=3s --retries=5 \
    CMD pg_isready -U $POSTGRES_USER -d $POSTGRES_DB || exit 1