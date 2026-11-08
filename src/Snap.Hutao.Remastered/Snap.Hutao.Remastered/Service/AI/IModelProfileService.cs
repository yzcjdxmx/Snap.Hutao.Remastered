// Copyright (c) 2026 DGP Studio. All rights reserved.
// Licensed under the MIT license.
// Adapted from Snap.Nicole.

using Snap.Hutao.Remastered.Service.AI.Models;

namespace Snap.Hutao.Remastered.Service.AI;

internal interface IModelProfileService
{
    Task<IReadOnlyList<ModelProfile>> GetModelsAsync(ModelProviderProfile providerProfile, CancellationToken cancellationToken);
}
