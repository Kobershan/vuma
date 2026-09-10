namespace VumaRetail.Domain.Crm;

/// <summary>A lead's position in the qualification pipeline (Stage 19).</summary>
public enum LeadStatus
{
    /// <summary>Captured, not yet worked.</summary>
    New = 0,

    /// <summary>Someone has reached out.</summary>
    Contacted = 1,

    /// <summary>Qualified as a real opportunity source.</summary>
    Qualified = 2,

    /// <summary>Converted into a customer. Terminal — a converted lead never re-opens.</summary>
    Converted = 3,

    /// <summary>Qualified out. Terminal.</summary>
    Disqualified = 4,

    /// <summary>Gone cold. Terminal.</summary>
    Dead = 5,
}

/// <summary>Where a lead came from.</summary>
public enum LeadSource
{
    /// <summary>Walked into the store.</summary>
    WalkIn = 0,

    /// <summary>Web site or storefront form.</summary>
    Web = 1,

    /// <summary>Referred by an existing customer.</summary>
    Referral = 2,

    /// <summary>Bulk import (Stage 11 pipeline).</summary>
    Import = 3,
}

/// <summary>An opportunity's position in the deal pipeline (Stage 19).</summary>
public enum OpportunityStage
{
    /// <summary>First contact, deal taking shape.</summary>
    Prospecting = 0,

    /// <summary>Being qualified.</summary>
    Qualification = 1,

    /// <summary>Proposal issued.</summary>
    Proposal = 2,

    /// <summary>Negotiating terms.</summary>
    Negotiation = 3,

    /// <summary>Won. Terminal — requires a customer.</summary>
    Won = 4,

    /// <summary>Lost. Terminal — requires a loss reason.</summary>
    Lost = 5,
}

/// <summary>What kind of interaction an activity records (Stage 19).</summary>
public enum ActivityType
{
    /// <summary>Phone call.</summary>
    Call = 0,

    /// <summary>Email.</summary>
    Email = 1,

    /// <summary>Meeting.</summary>
    Meeting = 2,

    /// <summary>Free-form note.</summary>
    Note = 3,

    /// <summary>In-person visit.</summary>
    Visit = 4,

    /// <summary>SMS message.</summary>
    Sms = 5,

    /// <summary>System-generated record (e.g. lead conversion). Never user-entered.</summary>
    System = 6,
}

/// <summary>Which way an interaction flowed (Stage 19).</summary>
public enum ActivityDirection
{
    /// <summary>The customer contacted us.</summary>
    Inbound = 0,

    /// <summary>We contacted the customer.</summary>
    Outbound = 1,
}

/// <summary>How a segment holds its members (Stage 19).</summary>
public enum SegmentKind
{
    /// <summary>Explicit member list in <c>crm.segment_members</c>.</summary>
    Static = 0,

    /// <summary>Evaluated at read time from <c>QueryExpression</c>. Never persisted per member.</summary>
    Dynamic = 1,
}

/// <summary>What a segment member points at (Stage 19).</summary>
public enum MemberType
{
    /// <summary>A Stage 06 partner/customer identity.</summary>
    Customer = 0,

    /// <summary>A CRM lead.</summary>
    Lead = 1,
}

/// <summary>What a consent record covers, per POPIA purpose limitation (Stage 19).</summary>
/// <remarks>
/// One row per (type, customer). A blanket "agree to everything" is not valid consent, so each
/// purpose is captured separately.
/// </remarks>
public enum ConsentType
{
    /// <summary>Marketing email.</summary>
    MarketingEmail = 0,

    /// <summary>Marketing SMS.</summary>
    MarketingSms = 1,

    /// <summary>Marketing push notification.</summary>
    MarketingPush = 2,

    /// <summary>Processing of personal data (gates cloud sync of loyalty data, Stage 20).</summary>
    DataProcessing = 3,

    /// <summary>Sharing with third parties.</summary>
    ThirdPartySharing = 4,

    /// <summary>Profiling / automated decisions.</summary>
    Profiling = 5,
}

/// <summary>A consent record's state (Stage 19).</summary>
public enum ConsentState
{
    /// <summary>Consent given and currently effective.</summary>
    Given = 0,

    /// <summary>Withdrawn by the data subject. Immediate, no grace period.</summary>
    Withdrawn = 1,

    /// <summary>Past <c>ExpiresAt</c>. Terminal until re-given.</summary>
    Expired = 2,

    /// <summary>Never asked. The default for a new customer.</summary>
    NotAsked = 3,
}
