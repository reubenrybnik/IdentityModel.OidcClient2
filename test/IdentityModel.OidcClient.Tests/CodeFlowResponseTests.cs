﻿// Copyright (c) Brock Allen & Dominick Baier. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.


using FluentAssertions;
using IdentityModel.OidcClient.Tests.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;
using JsonWebKeySet = IdentityModel.Jwk.JsonWebKeySet;

namespace IdentityModel.OidcClient.Tests
{
    public class CodeFlowResponseTests
    {
        OidcClientOptions _options = new OidcClientOptions
        {
            ClientId = "client",
            Scope = "openid profile api",
            RedirectUri = "https://redirect",

            Flow = OidcClientOptions.AuthenticationFlow.AuthorizationCode,
            LoadProfile = false,

            ProviderInformation = new ProviderInformation
            {
                IssuerName = "https://authority",
                AuthorizeEndpoint = "https://authority/authorize",
                TokenEndpoint = "https://authority/token",
                KeySet = new JsonWebKeySet()
            }
        };

        [Fact]
        public async Task valid_response_should_succeed()
        {
            var client = new OidcClient(_options);
            var state = await client.PrepareLoginAsync();

            var url = $"?state={state.State}&nonce={state.Nonce}&code=bar";
            var key = Crypto.CreateRsaKey();
            var signingCredentials = new SigningCredentials(key, "RS256");
            var idToken = Crypto.CreateJwt(signingCredentials, "https://authority", "client",
                new Claim("at_hash", Crypto.HashData("token")),
                new Claim("sub", "123"),
                new Claim("nonce", state.Nonce));

            var tokenResponse = new Dictionary<string, object>
            {
                { "access_token", "token" },
                { "expires_in", 300 },
                { "id_token", idToken },
                { "refresh_token", "refresh_token" }
            };

            _options.ProviderInformation.KeySet = Crypto.CreateKeySet(key);
            _options.BackchannelHandler = new NetworkHandler(JsonConvert.SerializeObject(tokenResponse), HttpStatusCode.OK);

            var result = await client.ProcessResponseAsync(url, state);

            result.IsError.Should().BeFalse();
            result.AccessToken.Should().Be("token");
            result.IdentityToken.Should().NotBeNull();
            result.User.Should().NotBeNull();
        }

        [Fact]
        public async Task multi_tenant_token_issuer_name_should_succeed_by_policy_option()
        {
            var client = new OidcClient(_options);
            var state = await client.PrepareLoginAsync();

            _options.Policy.Discovery.ValidateEndpoints = false;
            _options.Policy.ValidateTokenIssuerName = false;

            var url = $"?state={state.State}&nonce={state.Nonce}&code=bar";
            var key = Crypto.CreateRsaKey();
            var signingCredentials = new SigningCredentials(key, "RS256");
            var idToken = Crypto.CreateJwt(signingCredentials, "https://{some_multi_tenant_name}", "client",
                new Claim("at_hash", Crypto.HashData("token")),
                new Claim("sub", "123"),
                new Claim("nonce", state.Nonce));

            var tokenResponse = new Dictionary<string, object>
            {
                { "access_token", "token" },
                { "expires_in", 300 },
                { "id_token", idToken },
                { "refresh_token", "refresh_token" }
            };

            _options.ProviderInformation.KeySet = Crypto.CreateKeySet(key);
            _options.BackchannelHandler = new NetworkHandler(JsonConvert.SerializeObject(tokenResponse), HttpStatusCode.OK);

            var result = await client.ProcessResponseAsync(url, state);

            result.IsError.Should().BeFalse();
            result.AccessToken.Should().Be("token");
            result.IdentityToken.Should().NotBeNull();
            result.User.Should().NotBeNull();
        }

        [Fact]
        public async Task extra_parameters_on_backchannel_should_be_sent()
        {
            var client = new OidcClient(_options);
            var state = await client.PrepareLoginAsync();

            var url = $"?state={state.State}&nonce={state.Nonce}&code=bar";
            var key = Crypto.CreateRsaKey();
            var signingCredentials = new SigningCredentials(key, "RS256");
            var idToken = Crypto.CreateJwt(signingCredentials, "https://authority", "client",
                new Claim("at_hash", Crypto.HashData("token")),
                new Claim("sub", "123"),
                new Claim("nonce", state.Nonce));

            var tokenResponse = new Dictionary<string, object>
            {
                { "access_token", "token" },
                { "expires_in", 300 },
                { "id_token", idToken },
                { "refresh_token", "refresh_token" }
            };

            _options.ProviderInformation.KeySet = Crypto.CreateKeySet(key);
            var handler = new NetworkHandler(JsonConvert.SerializeObject(tokenResponse), HttpStatusCode.OK);
            _options.BackchannelHandler = handler;

            var extra = new
            {
                foo = "foo",
                bar = "bar"
            };

            var result = await client.ProcessResponseAsync(url, state, extra);

            result.IsError.Should().BeFalse();
            result.AccessToken.Should().Be("token");
            result.IdentityToken.Should().NotBeNull();
            result.User.Should().NotBeNull();

            var body = handler.Body;
            body.Should().Contain("foo=foo");
            body.Should().Contain("bar=bar");
        }

        [Fact]
        public async Task use_client_secret_for_id_token_validation_should_succeed()
        {
            var client = new OidcClient(_options);
            var state = await client.PrepareLoginAsync();

            var url = $"?state={state.State}&nonce={state.Nonce}&code=bar";
            var clientSecret = CryptoRandom.CreateRandomKeyString(32);
            var key = Crypto.CreateSymmetricKeyFromClientSecret(clientSecret);
            var signingCredentials = new SigningCredentials(key, "HS256");
            var idToken = Crypto.CreateJwt(signingCredentials, "https://authority", "client",
                new Claim("at_hash", Crypto.HashData("token")),
                new Claim("sub", "123"),
                new Claim("nonce", state.Nonce));

            var tokenResponse = new Dictionary<string, object>
            {
                { "access_token", "token" },
                { "expires_in", 300 },
                { "id_token", idToken },
                { "refresh_token", "refresh_token" }
            };

            _options.ClientSecret = clientSecret;
            _options.UseClientSecretForIdTokenValidation = true;
            _options.ProviderInformation.KeySet = Crypto.CreateKeySet(key);
            _options.Policy = new Policy
            {
                ValidSignatureAlgorithms = new[] { "HS256" }
            };
            _options.BackchannelHandler = new NetworkHandler(JsonConvert.SerializeObject(tokenResponse), HttpStatusCode.OK);

            var result = await client.ProcessResponseAsync(url, state);

            result.IsError.Should().BeFalse();
            result.AccessToken.Should().Be("token");
            result.IdentityToken.Should().NotBeNull();
            result.User.Should().NotBeNull();
        }

        [Fact]
        public async Task invalid_nonce_should_fail()
        {
            var client = new OidcClient(_options);
            var state = await client.PrepareLoginAsync();

            var url = $"?state={state.State}&nonce={state.Nonce}&code=bar";
            var key = Crypto.CreateRsaKey();
            var signingCredentials = new SigningCredentials(key, "RS256");
            var idToken = Crypto.CreateJwt(signingCredentials, "https://authority", "client",
                new Claim("at_hash", Crypto.HashData("token")),
                new Claim("sub", "123"),
                new Claim("nonce", "invalid"));

            var tokenResponse = new Dictionary<string, object>
            {
                { "access_token", "token" },
                { "expires_in", 300 },
                { "id_token", idToken },
                { "refresh_token", "refresh_token" }
            };

            _options.ProviderInformation.KeySet = Crypto.CreateKeySet(key);
            _options.BackchannelHandler = new NetworkHandler(JsonConvert.SerializeObject(tokenResponse), HttpStatusCode.OK);

            var result = await client.ProcessResponseAsync(url, state);

            result.IsError.Should().BeTrue();
            result.Error.Should().Be("Error validating token response: Invalid nonce.");
        }

        [Fact]
        public async Task missing_nonce_should_fail()
        {
            var client = new OidcClient(_options);
            var state = await client.PrepareLoginAsync();

            var url = $"?state={state.State}&nonce={state.Nonce}&code=bar";
            var key = Crypto.CreateRsaKey();
            var signingCredentials = new SigningCredentials(key, "RS256");
            var idToken = Crypto.CreateJwt(signingCredentials, "https://authority", "client",
                new Claim("at_hash", Crypto.HashData("token")),
                new Claim("sub", "123"));

            var tokenResponse = new Dictionary<string, object>
            {
                { "access_token", "token" },
                { "expires_in", 300 },
                { "id_token", idToken },
                { "refresh_token", "refresh_token" }
            };

            _options.ProviderInformation.KeySet = Crypto.CreateKeySet(key);
            _options.BackchannelHandler = new NetworkHandler(JsonConvert.SerializeObject(tokenResponse), HttpStatusCode.OK);

            var result = await client.ProcessResponseAsync(url, state);

            result.IsError.Should().BeTrue();
            result.Error.Should().Be("Error validating token response: Invalid nonce.");
        }


        [Fact]
        public async Task error_redeeming_code_should_fail()
        {
            _options.BackchannelHandler = new NetworkHandler(new Exception("error"));

            var client = new OidcClient(_options);
            var state = await client.PrepareLoginAsync();

            var url = $"?state={state.State}&code=bar";
            var result = await client.ProcessResponseAsync(url, state);

            result.IsError.Should().BeTrue();
            result.Error.Should().StartWith("Error redeeming code: error");
        }

        [Fact]
        public async Task missing_access_token_on_token_response_should_fail()
        {
            var tokenResponse = new Dictionary<string, object>
            {
                //{ "access_token", "token" },
                { "expires_in", 300 },
                { "id_token", "id_token" },
                { "refresh_token", "refresh_token" }
            };

            _options.BackchannelHandler = new NetworkHandler(JsonConvert.SerializeObject(tokenResponse), HttpStatusCode.OK);

            var client = new OidcClient(_options);
            var state = await client.PrepareLoginAsync();

            var url = $"?state={state.State}&code=bar";
            var result = await client.ProcessResponseAsync(url, state);

            result.IsError.Should().BeTrue();
            result.Error.Should().Be("Error validating token response: Access token is missing on token response.");
        }

        [Fact]
        public async Task missing_identity_token_on_token_response_should_fail()
        {
            var tokenResponse = new Dictionary<string, object>
            {
                { "access_token", "token" },
                { "expires_in", 300 },
                //{ "id_token", "id_token" },
                { "refresh_token", "refresh_token" }
            };

            _options.BackchannelHandler = new NetworkHandler(JsonConvert.SerializeObject(tokenResponse), HttpStatusCode.OK);

            var client = new OidcClient(_options);
            var state = await client.PrepareLoginAsync();

            var url = $"?state={state.State}&code=bar";
            var result = await client.ProcessResponseAsync(url, state);

            result.IsError.Should().BeTrue();
            result.Error.Should().Be("Error validating token response: Identity token is missing on token response.");
        }

        [Fact]
        public async Task malformed_identity_token_on_token_response_should_fail()
        {
            var tokenResponse = new Dictionary<string, object>
            {
                { "access_token", "token" },
                { "expires_in", 300 },
                { "id_token", "id_token" },
                { "refresh_token", "refresh_token" }
            };

            _options.BackchannelHandler = new NetworkHandler(JsonConvert.SerializeObject(tokenResponse), HttpStatusCode.OK);

            var client = new OidcClient(_options);
            var state = await client.PrepareLoginAsync();

            var url = $"?state={state.State}&code=bar";
            var result = await client.ProcessResponseAsync(url, state);

            result.IsError.Should().BeTrue();
            result.Error.Should().Contain("IDX12709: JWT is not well formed");
        }

        [Fact]
        public async Task no_keyset_for_identity_token_should_fail()
        {
            var tokenResponse = new Dictionary<string, object>
            {
                { "access_token", "token" },
                { "expires_in", 300 },
                { "id_token", Crypto.UntrustedIdentityToken },
                { "refresh_token", "refresh_token" }
            };

            _options.BackchannelHandler = new NetworkHandler(JsonConvert.SerializeObject(tokenResponse), HttpStatusCode.OK);

            var client = new OidcClient(_options);
            var state = await client.PrepareLoginAsync();

            var url = $"?state={state.State}&code=bar";
            var result = await client.ProcessResponseAsync(url, state);

            result.IsError.Should().BeTrue();
            result.Error.Should().StartWith("Error validating token response: Error validating identity token: Microsoft.IdentityModel.Tokens.SecurityTokenInvalidSignatureException: IDX10500: Signature validation failed. No security keys were provided to validate the signature");
        }

        [Fact]
        public async Task untrusted_identity_token_should_fail()
        {
            var tokenResponse = new Dictionary<string, object>
            {
                { "access_token", "token" },
                { "expires_in", 300 },
                { "id_token", Crypto.UntrustedIdentityToken },
                { "refresh_token", "refresh_token" }
            };

            _options.ProviderInformation.KeySet = Crypto.CreateKeySet(Crypto.CreateRsaKey());
            _options.BackchannelHandler = new NetworkHandler(JsonConvert.SerializeObject(tokenResponse), HttpStatusCode.OK);
            
            var client = new OidcClient(_options);
            var state = await client.PrepareLoginAsync();

            var url = $"?state={state.State}&code=bar";
            var result = await client.ProcessResponseAsync(url, state);

            result.IsError.Should().BeTrue();
            result.Error.Should().Contain("IDX10501: Signature validation failed");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task at_hash_policy_should_be_enforced(bool atHashRequired)
        {
            var client = new OidcClient(_options);
            var state = await client.PrepareLoginAsync();

            var url = $"?state={state.State}&nonce={state.Nonce}&code=bar";
            var key = Crypto.CreateRsaKey();
            var signingCredentials = new SigningCredentials(key, "RS256");
            var idToken = Crypto.CreateJwt(signingCredentials, "https://authority", "client",
                new Claim("sub", "123"),
                new Claim("nonce", state.Nonce));
                
            var tokenResponse = new Dictionary<string, object>
            {
                { "access_token", "token" },
                { "expires_in", 300 },
                { "id_token", idToken },
                { "refresh_token", "refresh_token" }
            };

            _options.ProviderInformation.KeySet = Crypto.CreateKeySet(key);
            _options.BackchannelHandler = new NetworkHandler(JsonConvert.SerializeObject(tokenResponse), HttpStatusCode.OK);
            _options.Policy.RequireAccessTokenHash = atHashRequired;

            var result = await client.ProcessResponseAsync(url, state);

            if (atHashRequired)
            {
                result.IsError.Should().BeTrue();
                result.Error.Should().Be("Error validating token response: at_hash is missing.");
            }
            else
            {
                result.IsError.Should().BeFalse();
                result.AccessToken.Should().Be("token");
                result.IdentityToken.Should().NotBeNull();
                result.User.Should().NotBeNull();
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task invalid_at_hash_should_fail(bool atHashRequired)
        {
            var client = new OidcClient(_options);
            var state = await client.PrepareLoginAsync();

            var url = $"?state={state.State}&nonce={state.Nonce}&code=bar";
            var key = Crypto.CreateRsaKey();
            var signingCredentials = new SigningCredentials(key, "RS256");
            var idToken = Crypto.CreateJwt(signingCredentials, "https://authority", "client",
                new Claim("at_hash", "invalid"),
                new Claim("sub", "123"),
                new Claim("nonce", state.Nonce));

            var tokenResponse = new Dictionary<string, object>
            {
                { "access_token", "token" },
                { "expires_in", 300 },
                { "id_token", idToken },
                { "refresh_token", "refresh_token" }
            };

            _options.ProviderInformation.KeySet = Crypto.CreateKeySet(key);
            _options.BackchannelHandler = new NetworkHandler(JsonConvert.SerializeObject(tokenResponse), HttpStatusCode.OK);
            _options.Policy.RequireAccessTokenHash = atHashRequired;

            var result = await client.ProcessResponseAsync(url, state);

            result.IsError.Should().BeTrue();
            result.Error.Should().Be("Error validating token response: Invalid access token hash.");
        }

        [Fact]
        public async Task invalid_signing_algorithm_should_fail()
        {
            var client = new OidcClient(_options);
            var state = await client.PrepareLoginAsync();

            var url = $"?state={state.State}&code=bar";
            var key = Crypto.CreateRsaKey();
            var signingCredentials = new SigningCredentials(key, "RS256");
            var idToken = Crypto.CreateJwt(signingCredentials, "https://authority", "client",
                new Claim("at_hash", Crypto.HashData("token")),
                new Claim("sub", "123"));

            var tokenResponse = new Dictionary<string, object>
            {
                { "access_token", "token" },
                { "expires_in", 300 },
                { "id_token", idToken },
                { "refresh_token", "refresh_token" }
            };

            _options.ProviderInformation.KeySet = Crypto.CreateKeySet(key);
            _options.BackchannelHandler = new NetworkHandler(JsonConvert.SerializeObject(tokenResponse), HttpStatusCode.OK);

            _options.Policy.ValidSignatureAlgorithms.Clear();
            _options.Policy.ValidSignatureAlgorithms.Add("unsupported");

            var result = await client.ProcessResponseAsync(url, state);

            result.IsError.Should().BeTrue();
            result.Error.Should().Be("Error validating token response: Identity token uses invalid algorithm: RS256");
        }
    }
}