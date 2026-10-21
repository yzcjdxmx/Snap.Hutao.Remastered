// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Remastered.Model.Metadata;
using Snap.Hutao.Remastered.Model.Metadata.Avatar;
using Snap.Hutao.Remastered.Model.Primitive;
using Snap.Hutao.Remastered.UI.Xaml.Control.TextBlock.Syntax.MiHoYo;
using System.Collections.Immutable;
using System.Globalization;

namespace Snap.Hutao.Remastered.ViewModel.Wiki;

public sealed class LinkMetadataContext
{
    public ImmutableDictionary<HyperLinkNameId, HyperLinkName> IdNameMap { get; init; } = default!;

    public ImmutableArray<ProudSkill> Skills { get; init; }

    public ImmutableArray<Skill> Talents { get; init; }

    public ImmutableArray<ProudSkill> Inherents { get; init; }

    public bool TryGetNameAndDescription(MiHoYoSyntaxLinkKind kind, uint id, out string name, out string description)
    {
        name = default!;
        description = default!;

        switch (kind)
        {
            case MiHoYoSyntaxLinkKind.Name:
                HyperLinkName hyperLinkName = IdNameMap[id];
                name = hyperLinkName.Name;
                description = hyperLinkName.Description;
                break;
            case MiHoYoSyntaxLinkKind.Inherent:
                ProudSkill inherent = Inherents.Single(s => s.Id == id);
                name = inherent.Name;
                description = inherent.Description;
                break;
            case MiHoYoSyntaxLinkKind.Skill:
                ProudSkill skill = Skills.Single(s => s.Id == id);
                name = skill.Name;
                description = skill.Description;
                break;
            case MiHoYoSyntaxLinkKind.Talent:
                Skill talent = Talents.Single(s => s.Id == id);
                name = talent.Name;
                description = talent.Description;
                break;
            default:
                return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves a <c>{PARAM#P1234567|2S100}</c> placeholder.
    /// The id references a proud skill parameter list, the one based index selects
    /// the parameter and the factor scales it, e.g. <c>5S100</c> means
    /// <c>parameters[4] * 100</c>.
    /// </summary>
    public bool TryGetParameter(MiHoYoSyntaxParameterKind kind, ReadOnlySpan<char> idSpan, out string value)
    {
        value = default!;

        if (!idSpan.TrySplitIntoTwo('|', out ReadOnlySpan<char> proudSkillIdSpan, out ReadOnlySpan<char> parameterSpan))
        {
            return false;
        }

        if (!uint.TryParse(proudSkillIdSpan[1..], out uint proudSkillId))
        {
            return false;
        }

        if (!parameterSpan.TrySplitIntoTwo('S', out ReadOnlySpan<char> oneBasedIndexSpan, out ReadOnlySpan<char> factorSpan))
        {
            return false;
        }

        if (!int.TryParse(oneBasedIndexSpan, out int oneBasedIndex) || oneBasedIndex <= 0)
        {
            return false;
        }

        if (!int.TryParse(factorSpan, out int factor))
        {
            return false;
        }

        if (kind is not MiHoYoSyntaxParameterKind.ProudSkill)
        {
            return false;
        }

        // Inherents are ascension passives, they are not a part of Skills, but their
        // parameters can still be referenced, e.g. 「救援要义」 references the
        // 「场中医者视野」 inherent by {PARAM#P1132101|5S100}
        return TryGetProudSkillParameter(Skills, proudSkillId, oneBasedIndex, factor, out value)
            || TryGetProudSkillParameter(Inherents, proudSkillId, oneBasedIndex, factor, out value);
    }

    private static bool TryGetProudSkillParameter(ImmutableArray<ProudSkill> skills, uint proudSkillId, int oneBasedIndex, int factor, out string value)
    {
        foreach (ProudSkill skill in skills)
        {
            if (TryGetProudSkillParameter(skill, proudSkillId, oneBasedIndex, factor, out value))
            {
                return true;
            }
        }

        value = default!;
        return false;
    }

    private static bool TryGetProudSkillParameter(ProudSkill skill, uint proudSkillId, int oneBasedIndex, int factor, out string value)
    {
        value = default!;

        foreach ((ProudSkillId skillId, ImmutableArray<float> parameters) in skill.Proud.Parameters.IdParameters)
        {
            if (skillId != proudSkillId)
            {
                continue;
            }

            if (oneBasedIndex > parameters.Length)
            {
                return false;
            }

            // G6 trims the noise introduced by the float storage, e.g. 0.0024 stands for 0.24%
            value = (parameters[oneBasedIndex - 1] * factor).ToString("G6", CultureInfo.CurrentCulture);
            return true;
        }

        return false;
    }
}