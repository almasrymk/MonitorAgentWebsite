# Load tests

`website.js` is a [k6](https://k6.io) script for the website API. Targets come from the plan: p95 under 300 ms for
the API, under 150 ms for the agents' update checks, and under 0.1% errors.

## Run it

Against the local docker compose stack (raises the per-IP rate limit and turns the sandbox payment provider on):

```bash
echo "SANDBOX_SECRET=$(openssl rand -hex 32)" >> .env
docker compose -f docker-compose.yml -f tests/load/compose.load.yml up -d --build
k6 run -e BASE_URL=http://localhost:5200 -e SANDBOX_SECRET=<the same secret> tests/load/website.js
```

Or from GitHub: Actions → Load test → Run workflow.

Against staging (numbers that count): leave `SANDBOX_SECRET` out unless staging uses the sandbox provider, add
`-e WITH_CATALOG=1` when staging is connected to LicensingPlatform, and ask whoever runs the WAF to allow the test
machine's IP, since all traffic comes from it.

## What is not here

- Device heartbeats and activations: those are LicensingPlatform's Agent API, so they are load tested in that repo.
- Sign-in and checkout: sign-in is deliberately limited to 10 attempts per minute per IP, and checkout needs confirmed
  accounts. Test them on staging with a list of prepared accounts and the rate limit raised for the test IP.
- Soak (8 hours) and stress: run `website.js` with longer stages on staging and compare memory and p95 at the start
  and the end (the plan allows at most 10% drift).
