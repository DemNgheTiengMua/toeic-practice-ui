# The MVC site proxies the Web API; the JWT never reaches browser script

The deliverable is two apps: an ASP.NET Core Web API (JWT bearer) and an ASP.NET Core MVC site whose
pages use jQuery Ajax. The JWT is held in an HttpOnly cookie issued by the MVC site, so page script
cannot read it. jQuery therefore calls the MVC site, which forwards each call to the API with the
bearer token attached. We rejected letting the browser call the API directly with the token in
`localStorage`, because any XSS bug would then leak the token.

## Consequences

- The Web API is never called from the browser; CORS stays closed.
- The MVC site needs anti-forgery protection on its forwarding endpoints, since the cookie rides along
  automatically.
