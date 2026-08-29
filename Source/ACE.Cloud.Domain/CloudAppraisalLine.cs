namespace ACE.Cloud.Domain;

/// <summary>One rendered line of appraisal text plus its typography token (never HTML).</summary>
public sealed record CloudAppraisalLine
{
    public required string Text { get; init; }

    public CloudAppraisalTextStyle Style { get; init; } = CloudAppraisalTextStyle.Body;
}
