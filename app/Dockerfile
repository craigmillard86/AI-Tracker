# Build context is the repo root (see docker-compose.yml).
FROM node:20-alpine
WORKDIR /app

# Install against the lockfile first so the layer caches across source edits.
COPY app/package.json app/package-lock.json ./
RUN npm ci

COPY app/ ./
EXPOSE 5173
CMD ["npm", "run", "dev", "--", "--host", "0.0.0.0"]
