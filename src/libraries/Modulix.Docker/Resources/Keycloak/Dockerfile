FROM quay.io/keycloak/keycloak:latest AS builder

ENV KC_DB=postgres \
    KC_HTTP_ENABLED=true \
    KC_PROXY_HEADERS=xforwarded

COPY modulix-realm.json /opt/keycloak/data/import/

RUN /opt/keycloak/bin/kc.sh build

FROM quay.io/keycloak/keycloak:latest
COPY --from=builder /opt/keycloak/ /opt/keycloak/

ENV KC_DB=postgres \
    KC_DB_URL=jdbc:postgresql://keycloak-db:5432/keycloak \
    KC_DB_USERNAME=keycloak \
    KC_DB_PASSWORD=1234 \
    KC_BOOTSTRAP_ADMIN_USERNAME=admin \
    KC_BOOTSTRAP_ADMIN_PASSWORD=1234 \
    KC_HTTP_ENABLED=true \
    KC_PROXY_HEADERS=xforwarded

ENTRYPOINT ["/opt/keycloak/bin/kc.sh"]
CMD ["start", "--optimized", "--import-realm"]