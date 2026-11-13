# The MVC site proxies the Web API; the JWT never reaches browser script

## Status
Superseded by [ADR-0015](0015-react-heroui-spa-and-cookie-jwt-api.md) (2026-11-13) — `src/WebMVC` is replaced by the `src/web` React 19 + HeroUI v3 SPA behind a same-origin `/api` and `/media` proxy, while preserving the `HttpOnly` JWT cookie invariant directly on `src/WebAPI`.

The deliverable is two apps: an ASP.NET Core Web API (JWT bearer) and an ASP.NET Core MVC site whose
pages use jQuery Ajax. The JWT is held in an HttpOnly cookie issued by the MVC site, so page script
cannot read it. jQuery therefore calls the MVC site, which forwards each call to the API with the
bearer token attached. We rejected letting the browser call the API directly with the token in
`localStorage`, because any XSS bug would then leak the token.

## Consequences

- The Web API is never called from the browser; CORS stays closed.
- The MVC site needs anti-forgery protection on its forwarding endpoints, since the cookie rides along
  automatically.
