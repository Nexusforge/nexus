- https is required for OIDC .well-known address
- developer certificate seems to be trusted automatically Linux for Edge browser
- developer certficates is not being trusted by Firefox
  1. the constraint "CA: true" is missing
  2. Firefox does not allow using the CA certifcate as end certificate
- .NET is aware of these issues and tries to improve experience for .net 7 so that `dev-certs https --trust` works everywhere
- however, dotnet watch uses a random port for the browser refresh and Firefox exceptions work for specific ports, so Firefox is unusable until the certificate trust issue is solved :-(
