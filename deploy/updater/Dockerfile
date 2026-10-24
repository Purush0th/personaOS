# PersonaOS updater (see updater.py): installs the release the API asks for. Runs as root because it
# talks to the Docker socket, which is why it does nothing but this and publishes no port.
FROM python:3.13-alpine

RUN apk add --no-cache docker-cli docker-cli-compose tar

WORKDIR /app
COPY updater.py .

ENTRYPOINT ["python", "-u", "/app/updater.py"]
