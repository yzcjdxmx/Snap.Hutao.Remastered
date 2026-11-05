// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.Extensions.Caching.Memory;
using Snap.Hutao.Remastered.Core.DependencyInjection.Annotation.HttpClient;
using Snap.Hutao.Remastered.Service.Yae.Achievement;
using Snap.Hutao.Remastered.Web.Endpoint.Hutao;
using Snap.Hutao.Remastered.Web.Request.Builder;
using Snap.Hutao.Remastered.Web.Request.Builder.Abstraction;
using System.Net.Http;

namespace Snap.Hutao.Remastered.Service.Feature;

[Service(ServiceLifetime.Singleton, typeof(IFeatureService))]
[HttpClient(HttpClientConfiguration.Default)]
public sealed partial class FeatureService : IFeatureService
{
    private readonly IHttpRequestMessageBuilderFactory httpRequestMessageBuilderFactory;
    private readonly IHutaoEndpointsFactory hutaoEndpointsFactory;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IMemoryCache memoryCache;

    [GeneratedConstructor]
    public partial FeatureService(IServiceProvider serviceProvider);

    public ValueTask<AchievementFieldId?> GetAchievementFieldIdAsync(string tag)
    {
        return GetTaggedFeatureAsync<AchievementFieldId>(tag, TimeSpan.FromHours(6));
    }

    private async ValueTask<TFeature?> GetTaggedFeatureAsync<TFeature>(string tag, TimeSpan expiration)
        where TFeature : class
    {
        string featureName = typeof(TFeature).Name;
        return await memoryCache.GetOrCreateAsync($"{nameof(FeatureService)}.{featureName}.{tag}", async entry =>
        {
            entry.SetSlidingExpiration(expiration);
            HttpRequestMessageBuilder builder = httpRequestMessageBuilderFactory
                .Create()
                .SetRequestUri(hutaoEndpointsFactory.Create().Feature($"{featureName}_{tag}"))
                .Get();

            using (HttpClient httpClient = httpClientFactory.CreateClient(nameof(FeatureService)))
            {
                TFeature? feature = (await builder.SendAsync<TFeature>(httpClient, CancellationToken.None).ConfigureAwait(false)).Body;
                if (feature is null)
                {
                    // A failed download/parse must be retryable immediately, not cached for six hours.
                    entry.AbsoluteExpiration = DateTimeOffset.MinValue;
                }

                return feature;
            }
        }).ConfigureAwait(false);
    }
}

