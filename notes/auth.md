# Authentication and Authorization

Nexus exposes resources (data, metadata and more) via HTTP API. Most of these resources do not have specific owners - they are owned by the system itself. Most of these resources need to be protected which makes an `authorization` mechanism necessary.

The first thing that comes into mind with HTTP services and authorization is `OAuth (v2)`. However, studying the specs reveals the following statement:
"In OAuth, the client requests access to resources `controlled by the resource owner` and hosted by the resource server [...]" [[RFC 6749](https://datatracker.ietf.org/doc/html/rfc6749)]

According to the RFC, OAuth is a mechanism for a user to grant a client (an application) access to their resources without revealing their own credentials and with the option to only grant a limited set of permissions.

**Example**: A user with a Microsoft account and access to OneDrive wants to allow another application (in terms of OAuth it's the _client_) to access the files that are on their drive. The user does not want to give this application a password and the user only wants to grant read permissions.

For this scenario, OAuth is a good option since every Microsoft user is a resource owner with the same set of rights to their own OneDrive files. OAuth helps limit the set of permissions to those actually required for that particular task (using the "scope" claim or other claims).

With Nexus, the situation is different. It's not like a user owns a resource that a client should access (here the client would be the front-end single page application (SPA) or a console application). Instead, the user himself wants to gain access to resources owned by the system.

When an API request is made, Nexus needs to know the user's identity to determine which resources can be accessed. Instead of using OAuth for authorization, Nexus will rely on [OpenID Connect](https://openid.net/specs/openid-connect-core-1_0.html) (based on OAuth) for `authentication` and will then perform authorization itself.

This works by first authenticating the user, then consulting a database to find the user's claims (e.g. a claim which describes which resource catalogs the user can access) and finally setting a cookie with the user's identity information.

## Summary
Using OAuth means that permissions are managed by the authorization server (in the form of scopes), so you need control over that server, which isn't always the case. Instead, OpenID connect allows authentication via a single sign-on (SSO) provider, while authorization is still controlled by Nexus.

## Disadvantages: 
- the cookie might become very large
- the SPA cannot extract the username from the encrypted cookie, an additional API call is required

## Other clients:
Non-browser clients could use the device code flow but that is not supported by Open ID Connect. Therefore, Nexus will offer an API for authenticated users to obtain an access and refresh token as a json file stream. This file should be stored somewhere where the non-browser application can access it. Nexus will add bearer authentication along with the cookie authentication middleware to support both scenarios.

## Security considerations
- The audience should be provided in the OpenID Connect provider configuration to allow Nexus to validate the `aud` claim [RFC 7519](https://www.rfc-editor.org/rfc/rfc7519#section-4.1.3). This is an important security measure as described [here](https://www.keycloak.org/docs/11.0/server_admin/#_audience).

## Implementation details

The backend of Nexus is a confidential client upon user request, it will perform the authorization code flow to obtain an ID token to authenticate and sign-in the user.

Nexus supports multiple OpenID Connect providers. See [Configuration] on how to add configuration values.

## Alternative approach (and why it did not work):

Since there are many examples on the web for SPA scenarios (and Nexus offers a SPA), it was considered to follow and apply them to Nexus. The approach is to run the authorization code flow in the SPA to obtain an access token. This access token is then forwarded to the resource server (here: the backend of Nexus) where only a JWT bearer token middleware is configured (no cookies required).

The problem now is that although the access token contains the subject claim, it is missing more information about the user like its name. This makes it hard to manage user specific claims from within Nexus.

Another problem is that Nexus cannot add these user-specific claims to the access token, which means that the user database must be consulted for every single request, resulting in a high disk load.

Also, a such client would be public which means it is possible to copy the `client_id` and use them in other clients, which might be problematic when there is limited traffic allowed .

The last problem with refresh tokens is that _"for public clients [they] MUST be sender-constrained or use
   refresh token rotation [...]"_ [[OAuth 2.0 Security Best Current Practice](https://datatracker.ietf.org/doc/html/draft-ietf-oauth-security-topics-19#section-2.2.2), [RFC 6749](https://datatracker.ietf.org/doc/html/rfc6749#section-4.13)].

To solve the first issue, one might think that the ID token could be used instead of the access token but that is forbidden: _"Access tokens should only be used to make requests to the resource server. Additionally, ID tokens must not be used to make requests to the resource server."_ [[oauth.net](https://oauth.net/2/access-tokens/)]. Additionally the access token claims (e.g. scope) would be missing.

In the end it's clear that while there is nice OpenID connection support for Blazor wasm SPA, this approach doesn't suit Nexus.

## Other findings (informative):

### Scopes:

>_Scopes represent what a client application is allowed to do. They represent the scoped access [...] mentioned before. In IdentityServer, scopes are typically modeled as resources, which come in two flavors: identity and API._

>_An identity resource allows you to model a scope that will permit a client application to view a subset of claims about a user. For example, the profile scope enables the app to see claims about the user such as name and date of birth._

>_An API resource allows you to model access to an entire protected resource, an API, with individual permissions levels (scopes) that a client application can request access to."_ [[Source](https://www.scottbrady91.com/identity-server/getting-started-with-identityserver-4)]

When during an OAuth flow an API scope is requested, it will become part of the scope claim of the returned access token. Additionally, the audience claim will be set to the resource the scope belongs to. This is important to understand the audience validation.

When an API scope is requested during an OAuth flow, it becomes part of the `scope` claim of the returned access token. Also, the audience (`aud`) claim is set to the resource that the scope belongs to. This is important to understand audience validation. The audience value should identify the recipient of the access token, i.e. the resource server.

### Bearer token validation:
Bearer token validation does not necessarily require a manually provided token signing key for validation. The .NET middleware tries to get the public key from the authorization server on the first request [[Source](https://stackoverflow.com/questions/58758198/does-addjwtbearer-do-what-i-think-it-does)].