# Running the whole product in containers

One command brings up SQL Server, the schema, the demo data, `WebAPI` and `WebMVC` on a machine that
has never had SQL Server installed. This is for a developer's machine and for review; it is not a
production deployment (`docs/specs/2026-11-12-refunds-realtime-dashboard-swagger-docker.md`,
"Out of Scope").

| Thing | Value |
| :--- | :--- |
| Web app (start here) | <http://localhost:5000> |
| API | <http://localhost:5001> |
| API health | <http://localhost:5001/healthz> |
| API documentation | <http://localhost:5001/scalar> → document at `/openapi/v1.json` |

Accounts and seeded catalog: `docs/MANUAL-TEST-CREDENTIALS.md` — password `Demo1234!` for all.

---

## 1. Bring it up

Requires Docker with Linux containers (Docker Desktop, or any Docker Engine with compose v2).

```sh
cp .env.docker.example .env.docker
```

Open `.env.docker` and fill in `Jwt__SigningKey`. The API **refuses to start** without a base64 key
that decodes to at least 32 bytes, so generate one:

```sh
openssl rand -base64 32                    # Linux, macOS, Git Bash
```

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))   # PowerShell
```

Then:

```sh
docker compose up -d --build
docker compose ps                          # wait until api is healthy and mvc is running
```

The first run takes a few minutes: it pulls the SQL Server image and loads a 330 KB schema plus a
540 KB demo seed before either app starts. `docker compose logs -f db-init` shows that progress.

`admin@demo.local` / `Demo1234!` signs in; the dashboard and `/Admin` come up with the seeded data.

### What comes up

```
db        SQL Server 2022 — the database (volume: mssql-data)
  │
  ├── db-init   one-shot: create the database, load the schema, load the demo seed, exit 0
  │
  ├── api     WebAPI on :5001 — waits for db-init to exit 0 (volume: api-audio)
  │             healthcheck: curl http://localhost:5001/healthz
  │
  └── mvc     WebMVC on :5000 — waits for db-init to exit 0 and for api to be healthy
```

`WebMVC` calls `WebAPI` over the compose network at `http://api:5001`. The browser never talks to the
API directly (ADR-0002), which is why only `5000` needs to be reachable by a human.

### Stop, keep data

```sh
docker compose stop        # or `docker compose down` — volumes survive both
```

A restart keeps the database and the uploaded audio, and **does not reload the schema**: `db-init`
checks for the schema before it touches anything and exits with "already loaded".

---

## 2. Apply a schema change

The project is database-first and has no migration engine, so the loader recreates a database rather
than upgrading one. After editing `docs/database-schema.sql`:

```sh
docker compose down -v     # -v discards mssql-data AND api-audio
docker compose up -d --build
```

`down -v` is the whole procedure. There is no incremental path by design — do not try to patch a
running container's database, because the next teammate to run `down -v` gets a different one.

**This also deletes uploaded audio**, since `-v` drops both volumes. To rebuild only the database,
delete that one volume:

```sh
docker compose down
docker volume rm extra-efficiency_mssql-data
docker compose up -d --build
```

---

## 3. Switch the payment gateway

`.env.docker` carries `Payment__Gateway=mock`. The mock gateway cannot charge anyone, which is why it
is the default: a teammate must be able to run the stack immediately, and a stack that 500s on
checkout is not a stack that runs.

To test a real payOS checkout, change that one line and fill the four `PAYOS_*` values:

```diff
-Payment__Gateway=mock
+Payment__Gateway=payos
```

```sh
docker compose up -d api
```

`PAYOS_WEBHOOK_URL` must be reachable from the public internet — payOS's servers call it, so
`localhost` will never receive a callback. Put the container behind a tunnel if you need the callback.

---

## 4. Switch the API documentation off

`OpenApi:Enabled` is **on** by default, and while it is on the API serves:

- `GET /openapi/v1.json` — the document
- `GET /scalar` — the browsable UI
- `POST /api/auth/demo-sign-in` — a token for the demo admin account, **with no credentials**

That last route is a complete back door. It is a deliberate review aid on a laptop and it is
unacceptable on any host reachable from the internet. One flag removes all three:

```diff
-OpenApi__Enabled=true
+OpenApi__Enabled=false
```

```sh
docker compose up -d api
```

Confirm all three answer `404`:

```sh
for p in /openapi/v1.json /scalar /api/auth/demo-sign-in; do
  printf '%s ' "$p"; curl -s -o /dev/null -w '%{http_code}\n' "http://localhost:5001$p"
done
```

---

## 5. Notes for the curious

- **One Dockerfile, two targets.** `api` and `mvc` share four of six projects, so they share one
  restore and one compile stage. Two Dockerfiles would drift.
- **Only the API has a volume.** `WebMVC` serves committed assets from `wwwroot` and writes nothing.
  The API's audio store is the only writable media path in the product.
- **`Https__RedirectHttps=false` in compose.** No certificate is baked into an image, so the apps
  publish plain HTTP and the unconditional redirect would point at a port nothing listens on. The
  flag defaults to `true`, so a non-container run behaves exactly as before.
- **`AI_PROVIDER=fake` in the template.** Grading and insights come from deterministic offline fakes:
  no key, no quota. Set `AI_PROVIDER=real` and supply the keys to use the real providers.
- **Email is written to a folder,** not sent. `DropFolderEmailSender` writes `.eml` files inside the
  `api` container; `docker compose exec api ls /app/mail-drop` finds activation and reset links.
