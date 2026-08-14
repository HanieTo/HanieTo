FROM python:3.12-slim

WORKDIR /bot
COPY . .
RUN apt-get update && apt-get install -y \
    gcc \
    && rm -rf /var/lib/apt/lists/*
RUN pip install --no-cache-dir -r requirements.txt
# Pre-download the ngrok binary into the image (used only in dev mode - see
# utils/utils.py:start_ngrok). Without this, pyngrok re-downloads it on every
# container start, which is slow and fails outright on a flaky connection -
# every container recreation was crash-looping on this. Baking it in here
# means the download only has to succeed once, at build time, with retries.
RUN for i in 1 2 3 4 5; do \
        python -c "from pyngrok import ngrok; ngrok.install_ngrok()" && break || sleep 5; \
    done
ENV PYTHONUNBUFFERED=1
CMD ["python", "-u", "run.py"]