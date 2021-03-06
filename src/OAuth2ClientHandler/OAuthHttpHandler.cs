﻿using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using OAuth2ClientHandler.Authorizer;

namespace OAuth2ClientHandler
{
    public class OAuthHttpHandler : DelegatingHandler
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly Lazy<HttpClientHandler> _defaultHttpHandler = new Lazy<HttpClientHandler>(() => new HttpClientHandler());
        private readonly IAuthorizer _authorizer;
        private TokenResponse _tokenResponse;

        public OAuthHttpHandler(OAuthHttpHandlerOptions options, Func<HttpClient> createAuthorizerHttpClient = null)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            if (options.InnerHandler != null)
                InnerHandler = options.InnerHandler;

            _authorizer = new Authorizer.Authorizer(options.AuthorizerOptions, createAuthorizerHttpClient ?? CreateHttpClient);
        }

        private HttpClient CreateHttpClient() => new HttpClient(InnerHandler ?? _defaultHttpHandler.Value, false);

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing && _defaultHttpHandler.IsValueCreated)
                _defaultHttpHandler.Value.Dispose();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization == null)
            {
                var tokenResponse = await GetTokenResponse(cancellationToken);
                if (tokenResponse != null)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResponse.AccessToken);
            }

            var response = await base.SendAsync(request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Unauthorized) return response;
            {
                var tokenResponse = await RefreshTokenResponse(cancellationToken);
                if (tokenResponse != null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResponse.AccessToken);
                    response = await base.SendAsync(request, cancellationToken);
                }
            }

            return response;
        }

        private async Task<TokenResponse> GetTokenResponse(CancellationToken cancellationToken)
        {
            try
            {
                _semaphore.Wait(cancellationToken);
                if (cancellationToken.IsCancellationRequested) return null;
                _tokenResponse = _tokenResponse ?? await _authorizer.GetToken(cancellationToken);
                return _tokenResponse;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task<TokenResponse> RefreshTokenResponse(CancellationToken cancellationToken)
        {
            try
            {
                _semaphore.Wait(cancellationToken);
                if (cancellationToken.IsCancellationRequested) return null;
                _tokenResponse = await _authorizer.GetToken(cancellationToken);
                return _tokenResponse;
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
