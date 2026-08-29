using System.Globalization;

namespace ACE.Cloud.Domain;

/// <summary>
/// The single pure snapshot-to-presentation model for a Full Cloud Appraisal (UI-004, AUTH-001,
/// EVT-002): reproduces ACE's player-facing appraisal section order, wording, and colors/flags from a
/// <see cref="CloudAppraisalRawItemSnapshot"/> alone. <see cref="Build"/> intentionally takes no
/// examiner, character, skill, or Display Character parameter -- there is nothing in its signature a
/// caller could even pass to make the result vary by who is asking, so the panel is always a complete
/// successful appraisal exactly as CONTEXT.md requires ("Owners and authorized viewers receive the
/// same Full Cloud Appraisal without Display Character or appraisal-skill gating").
/// </summary>
public static class CloudAppraisalProjector
{
    public static CloudAppraisalPanel Build(CloudAppraisalRawItemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var sections = new List<CloudAppraisalSection>();

        AddSection(sections, CloudAppraisalSectionKind.Header, BuildHeaderLines(snapshot));
        AddSection(sections, CloudAppraisalSectionKind.Description, BuildDescriptionLines(snapshot));
        AddSection(sections, CloudAppraisalSectionKind.Requirements, BuildRequirementLines(snapshot));
        AddSection(sections, CloudAppraisalSectionKind.Activation, BuildActivationLines(snapshot));
        AddSection(sections, CloudAppraisalSectionKind.ArmorProtection, BuildArmorLines(snapshot.ArmorProfile));
        AddSection(sections, CloudAppraisalSectionKind.WeaponStatistics, BuildWeaponLines(snapshot.WeaponProfile));
        AddSection(sections, CloudAppraisalSectionKind.Spells, BuildSpellLines(snapshot.Spells));
        AddSection(sections, CloudAppraisalSectionKind.ValueAndBurden, BuildValueAndBurdenLines(snapshot));
        AddSection(sections, CloudAppraisalSectionKind.SpecialProperties, BuildSpecialPropertyLines(snapshot));

        return new CloudAppraisalPanel
        {
            ItemName = snapshot.Name,
            Sections = sections,
        };
    }

    private static void AddSection(List<CloudAppraisalSection> sections, CloudAppraisalSectionKind kind, IReadOnlyList<CloudAppraisalLine> lines)
    {
        if (lines.Count > 0)
        {
            sections.Add(new CloudAppraisalSection { Kind = kind, Lines = lines });
        }
    }

    private static IReadOnlyList<CloudAppraisalLine> BuildHeaderLines(CloudAppraisalRawItemSnapshot snapshot) =>
        [new CloudAppraisalLine { Text = snapshot.Name, Style = CloudAppraisalTextStyle.Title }];

    private static IReadOnlyList<CloudAppraisalLine> BuildDescriptionLines(CloudAppraisalRawItemSnapshot snapshot)
    {
        var lines = new List<CloudAppraisalLine>();

        if (!string.IsNullOrWhiteSpace(snapshot.LongDescription))
        {
            lines.Add(new CloudAppraisalLine { Text = snapshot.LongDescription, Style = CloudAppraisalTextStyle.Body });
        }

        if (!string.IsNullOrWhiteSpace(snapshot.UseDescription))
        {
            lines.Add(new CloudAppraisalLine { Text = $"Use: {snapshot.UseDescription}", Style = CloudAppraisalTextStyle.Body });
        }

        // Malformed-combination guard: a workmanship level with no meaningful lower bound is dropped
        // rather than rendered as a nonsensical negative quality.
        if (snapshot.Workmanship is >= 0)
        {
            lines.Add(new CloudAppraisalLine { Text = $"Workmanship: {snapshot.Workmanship}", Style = CloudAppraisalTextStyle.Muted });
        }

        if (!string.IsNullOrWhiteSpace(snapshot.MaterialName))
        {
            lines.Add(new CloudAppraisalLine { Text = $"Material: {snapshot.MaterialName}", Style = CloudAppraisalTextStyle.Muted });
        }

        // Malformed-combination guard: a gem count without a gem name (or vice versa) is dropped
        // rather than rendering a partial/nonsensical "0 " or "null" sentence.
        if (!string.IsNullOrWhiteSpace(snapshot.GemName) && snapshot.GemCount is > 0)
        {
            var plural = snapshot.GemCount == 1 ? string.Empty : "s";
            lines.Add(new CloudAppraisalLine
            {
                Text = $"Adorned with {snapshot.GemCount.Value.ToString(CultureInfo.InvariantCulture)} {snapshot.GemName}{plural}.",
                Style = CloudAppraisalTextStyle.Muted,
            });
        }

        return lines;
    }

    private static IReadOnlyList<CloudAppraisalLine> BuildRequirementLines(CloudAppraisalRawItemSnapshot snapshot)
    {
        var lines = new List<CloudAppraisalLine>();

        foreach (var requirement in snapshot.WieldRequirements)
        {
            // Malformed-combination guard: a non-positive minimum requirement is never real ACE data.
            if (requirement.MinimumValue <= 0)
            {
                continue;
            }

            var minimum = requirement.MinimumValue.ToString(CultureInfo.InvariantCulture);

            var text = requirement.Kind switch
            {
                CloudAppraisalWieldRequirementKind.CharacterLevel =>
                    $"Wielder must be level {minimum} or higher.",
                CloudAppraisalWieldRequirementKind.Skill when !string.IsNullOrWhiteSpace(requirement.SkillOrAttributeName) =>
                    $"Wielder must have {requirement.SkillOrAttributeName} skill of {minimum} or higher.",
                CloudAppraisalWieldRequirementKind.Attribute when !string.IsNullOrWhiteSpace(requirement.SkillOrAttributeName) =>
                    $"Wielder must have {requirement.SkillOrAttributeName} of {minimum} or higher.",
                // Malformed-combination guard: a Skill/Attribute requirement with no name cannot be worded.
                _ => null,
            };

            if (text is not null)
            {
                lines.Add(new CloudAppraisalLine { Text = text, Style = CloudAppraisalTextStyle.Body });
            }
        }

        // The allowed-wielder/activator instance IDs themselves are never player-facing (redaction);
        // only their presence becomes this boolean line, mirroring AppraiseInfo's own
        // AppraisalHasAllowedWielder/AppraisalHasAllowedActivator handling.
        if (snapshot.AllowedWielderInstanceId is > 0)
        {
            lines.Add(new CloudAppraisalLine { Text = "This item can only be wielded by an allowed character.", Style = CloudAppraisalTextStyle.Muted });
        }

        if (snapshot.AllowedActivatorInstanceId is > 0)
        {
            lines.Add(new CloudAppraisalLine { Text = "This item can only be activated by an allowed character.", Style = CloudAppraisalTextStyle.Muted });
        }

        return lines;
    }

    private static IReadOnlyList<CloudAppraisalLine> BuildActivationLines(CloudAppraisalRawItemSnapshot snapshot)
    {
        var lines = new List<CloudAppraisalLine>();

        if (snapshot.Spellcraft is >= 0)
        {
            lines.Add(new CloudAppraisalLine { Text = $"Spellcraft: {snapshot.Spellcraft}", Style = CloudAppraisalTextStyle.Body });
        }

        if (snapshot.CastingDifficulty is >= 0)
        {
            lines.Add(new CloudAppraisalLine { Text = $"Difficulty: {snapshot.CastingDifficulty}", Style = CloudAppraisalTextStyle.Body });
        }

        if (snapshot.ManaCost is >= 0)
        {
            lines.Add(new CloudAppraisalLine { Text = $"Mana Cost: {snapshot.ManaCost}", Style = CloudAppraisalTextStyle.Body });
        }

        if (snapshot.CurrentMana is >= 0 && snapshot.MaxMana is > 0)
        {
            lines.Add(new CloudAppraisalLine { Text = $"Mana: {snapshot.CurrentMana} / {snapshot.MaxMana}", Style = CloudAppraisalTextStyle.Body });
        }

        return lines;
    }

    private static IReadOnlyList<CloudAppraisalLine> BuildArmorLines(CloudAppraisalArmorProfile? profile)
    {
        if (profile is null)
        {
            return [];
        }

        return
        [
            StatLine("Armor Level", profile.ArmorLevel),
            StatLine("Slashing Protection", profile.Slashing),
            StatLine("Piercing Protection", profile.Piercing),
            StatLine("Bludgeoning Protection", profile.Bludgeoning),
            StatLine("Cold Protection", profile.Cold),
            StatLine("Fire Protection", profile.Fire),
            StatLine("Acid Protection", profile.Acid),
            StatLine("Nether Protection", profile.Nether),
            StatLine("Lightning Protection", profile.Lightning),
        ];
    }

    private static IReadOnlyList<CloudAppraisalLine> BuildWeaponLines(CloudAppraisalWeaponProfile? profile)
    {
        if (profile is null)
        {
            return [];
        }

        var lines = new List<CloudAppraisalLine>
        {
            new() { Text = $"Damage Type: {profile.DamageType}", Style = CloudAppraisalTextStyle.Body },
            new() { Text = $"Attack Skill: {profile.Skill.ToDisplayName()}", Style = CloudAppraisalTextStyle.Body },
            StatLine("Damage", profile.Damage),
            StatLine("Weapon Speed", profile.Speed),
            StatLine("Damage Variance", profile.DamageVariance),
            StatLine("Damage Modifier", profile.DamageModifier),
            StatLine("Attack Bonus", profile.AttackSkillBonus),
            StatLine("Defense Bonus", profile.DefenseBonus),
        };

        if (profile.ManaConversionModifier is { } manaConversion)
        {
            lines.Add(StatLine("Mana Conversion Modifier", manaConversion));
        }

        if (profile.ElementalDamageModifier is { } elementalDamage)
        {
            lines.Add(StatLine("Elemental Damage Modifier", elementalDamage));
        }

        return lines;
    }

    private static IReadOnlyList<CloudAppraisalLine> BuildSpellLines(IReadOnlyList<CloudAppraisalSpellReference> spells)
    {
        var lines = new List<CloudAppraisalLine>();

        foreach (var spell in spells)
        {
            // Malformed-combination guard: a spell entry with no resolvable name is never rendered.
            if (string.IsNullOrWhiteSpace(spell.Name))
            {
                continue;
            }

            var style = spell switch
            {
                { IsActiveEnchantment: true, IsHarmful: true } => CloudAppraisalTextStyle.Negative,
                { IsActiveEnchantment: true, IsHarmful: false } => CloudAppraisalTextStyle.Positive,
                _ => CloudAppraisalTextStyle.Body,
            };

            lines.Add(new CloudAppraisalLine { Text = spell.Name, Style = style });
        }

        if (lines.Count == 0)
        {
            return [];
        }

        lines.Insert(0, new CloudAppraisalLine { Text = "Item Enchantments:", Style = CloudAppraisalTextStyle.Muted });
        return lines;
    }

    private static IReadOnlyList<CloudAppraisalLine> BuildValueAndBurdenLines(CloudAppraisalRawItemSnapshot snapshot)
    {
        var lines = new List<CloudAppraisalLine>();

        // Malformed-combination guard: a negative value/burden is never real ACE data.
        if (snapshot.Value is >= 0)
        {
            lines.Add(new CloudAppraisalLine { Text = $"Value: {snapshot.Value}", Style = CloudAppraisalTextStyle.Body });
        }

        if (snapshot.Burden is >= 0)
        {
            lines.Add(new CloudAppraisalLine { Text = $"Burden: {snapshot.Burden}", Style = CloudAppraisalTextStyle.Body });
        }

        return lines;
    }

    private static IReadOnlyList<CloudAppraisalLine> BuildSpecialPropertyLines(CloudAppraisalRawItemSnapshot snapshot)
    {
        var lines = new List<CloudAppraisalLine>();

        if (snapshot.IsAttunedOrSticky)
        {
            lines.Add(new CloudAppraisalLine
            {
                Text = "This item is Attuned and cannot be traded, sold, or given to another player.",
                Style = CloudAppraisalTextStyle.Negative,
            });
        }

        if (snapshot.IsBonded)
        {
            lines.Add(new CloudAppraisalLine
            {
                Text = "This item is Bonded and will be destroyed if lost or intentionally dropped.",
                Style = CloudAppraisalTextStyle.Negative,
            });
        }

        return lines;
    }

    private static CloudAppraisalLine StatLine(string label, CloudAppraisalStatValue stat)
    {
        var style = stat switch
        {
            { IsHighlighted: true, IsBuffed: true } => CloudAppraisalTextStyle.Positive,
            { IsHighlighted: true, IsBuffed: false } => CloudAppraisalTextStyle.Negative,
            _ => CloudAppraisalTextStyle.Body,
        };

        return new CloudAppraisalLine
        {
            Text = $"{label}: {stat.Value.ToString("0.##", CultureInfo.InvariantCulture)}",
            Style = style,
        };
    }
}
