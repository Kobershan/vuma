#pragma warning disable CS1591
using VumaRetail.Domain.Entities;
using VumaRetail.Domain.Primitives;

namespace VumaRetail.Domain.Crm;

/// <summary>
/// Scaffolding Lead entity for Stage 19 CRM module.
/// </summary>
public sealed class Lead : Entity
{
    private Lead() { }

    /// <summary>Creates a lead.</summary>
    public Lead(Guid tenantId, string firstName, string lastName, string email,
        string? phone, string? company, LeadSource source) : base(tenantId)
    {
        FirstName = firstName; LastName = lastName; Email = email;
        Phone = phone; Company = company; Source = source; Status = LeadStatus.New;
    }

    /// <summary>First name.</summary>
    public string FirstName { get; private set; } = string.Empty;
    /// <summary>Last name.</summary>
    public string LastName { get; private set; } = string.Empty;
    /// <summary>Email.</summary>
    public string Email { get; private set; } = string.Empty;
    /// <summary>Phone.</summary>
    public string? Phone { get; private set; }
    /// <summary>Company.</summary>
    public string? Company { get; private set; }
    /// <summary>Source.</summary>
    public LeadSource Source { get; private set; }
    /// <summary>Status.</summary>
    public LeadStatus Status { get; private set; }
    /// <summary>Converted at.</summary>
    public DateTimeOffset? ConvertedAt { get; private set; }

    /// <summary>Converts the lead.</summary>
    public ConvertLeadResult Convert()
    {
        if (Status == LeadStatus.Converted)
        {
            throw new LeadAlreadyConvertedException();
        }

        Status = LeadStatus.Converted;
        ConvertedAt = DateTimeOffset.UtcNow;
        return new ConvertLeadResult(UuidV7.NewGuid(), this);
    }

    /// <summary>Marks converted (test hook).</summary>
    public void MarkConverted() => Status = LeadStatus.Converted;
}

/// <summary>Lead status.</summary>
public enum LeadStatus { New, Contacted, Qualified, Converted, Disqualified, Dead }
/// <summary>Lead source.</summary>
public enum LeadSource { WalkIn, Web, Referral, Import }

/// <summary>Conversion result.</summary>
public sealed class ConvertLeadResult
{
    /// <summary>Creates result.</summary>
    public ConvertLeadResult(Guid customerId, Lead lead)
    {
        ArgumentNullException.ThrowIfNull(lead);
        CustomerId = customerId; LeadStatus = lead.Status; ConvertedAt = lead.ConvertedAt;
    }
    /// <summary>Customer id.</summary>
    public Guid CustomerId { get; }
    /// <summary>Lead status.</summary>
    public LeadStatus LeadStatus { get; }
    /// <summary>Converted at.</summary>
    public DateTimeOffset? ConvertedAt { get; }
}

/// <summary>Already converted.</summary>
public sealed class LeadAlreadyConvertedException : DomainException
{
    /// <summary>Creates.</summary>
    public LeadAlreadyConvertedException() : base("LEAD_ALREADY_CONVERTED", "A lead that has already been converted cannot be converted again.") { }
}

/// <summary>Scaffolding Activity entity.</summary>
public sealed class Activity : Entity, IImmutableRecord
{
    private Activity() { }
    /// <summary>Creates.</summary>
    public Activity(Guid tenantId, ActivityType type, string subject, string? body) : base(tenantId)
    {
        ActivityType = type; Subject = subject; Body = body; HappenedAt = DateTimeOffset.UtcNow;
    }
    /// <summary>Type.</summary>
    public ActivityType ActivityType { get; }
    /// <summary>Subject.</summary>
    public string Subject { get; private set; } = string.Empty;
    /// <summary>Body.</summary>
    public string? Body { get; private set; }
    /// <summary>Happened at.</summary>
    public DateTimeOffset HappenedAt { get; }
    /// <summary>Updated at.</summary>
    public new DateTimeOffset UpdatedAt { get; private set; }
    /// <summary>Updated by.</summary>
    public new string UpdatedBy { get; private set; } = string.Empty;
    /// <summary>Marks updated.</summary>
    public new void MarkUpdated(string updatedBy, DateTimeOffset updatedAt) { UpdatedBy = updatedBy; UpdatedAt = updatedAt; }
    /// <summary>Refused.</summary>
    public void MarkDeleted() => throw new ActivityImmutableException();
    /// <summary>Refused.</summary>
    public void UpdateBody(string body) => throw new ActivityImmutableException();
    /// <summary>Refused.</summary>
    public void UpdateSubject(string subject) => throw new ActivityImmutableException();
    /// <summary>Refused.</summary>
    public void UpdateType(ActivityType type) => throw new ActivityImmutableException();
}

/// <summary>Activity type.</summary>
public enum ActivityType { Call, Email, Meeting, Note, Visit, Sms, System }

/// <summary>Scaffolding Segment.</summary>
public sealed class Segment
{
    /// <summary>Creates.</summary>
    public Segment(Guid tenantId, string name, SegmentKind kind, bool isDynamic = false)
    {
        if (kind == SegmentKind.Static && isDynamic)
        {
            throw new SegmentMixedKindException();
        }

        TenantId = tenantId; Name = name; Kind = isDynamic ? SegmentKind.Dynamic : kind; IsActive = true;
    }
    /// <summary>Tenant.</summary>
    public Guid TenantId { get; }
    /// <summary>Name.</summary>
    public string Name { get; }
    /// <summary>Kind.</summary>
    public SegmentKind Kind { get; }
    /// <summary>Active.</summary>
    public bool IsActive { get; private set; }
    private readonly List<(Guid MemberId, MemberType Type)> _members = [];
    /// <summary>Adds member.</summary>
    public void AddMember(Guid memberId, MemberType type)
    {
        if (Kind == SegmentKind.Dynamic)
        {
            throw new DynamicSegmentWriteNotAllowedException();
        }

        _members.Add((memberId, type));
    }
    /// <summary>Checks membership.</summary>
    public bool IsMember(Guid memberId, MemberType type)
    {
        if (!IsActive)
        {
            return false;
        }

        return Kind == SegmentKind.Static && _members.Any(m => m.MemberId == memberId && m.Type == type);
    }
    /// <summary>Deactivates.</summary>
    public void Deactivate() => IsActive = false;
}

/// <summary>Segment kind.</summary>
public enum SegmentKind { Static, Dynamic }
/// <summary>Member type.</summary>
public enum MemberType { Customer, Lead }

/// <summary>Mixed kind refused.</summary>
public sealed class SegmentMixedKindException : DomainException
{
    /// <summary>Creates.</summary>
    public SegmentMixedKindException() : base("SEGMENT_MIXED_KIND", "A segment cannot be both static and dynamic.") { }
}

/// <summary>Dynamic write refused.</summary>
public sealed class DynamicSegmentWriteNotAllowedException : DomainException
{
    /// <summary>Creates.</summary>
    public DynamicSegmentWriteNotAllowedException() : base("SEGMENT_DYNAMIC_WRITE_NOT_ALLOWED", "Dynamic segments cannot have members added directly.") { }
}

/// <summary>Activity immutable.</summary>
public sealed class ActivityImmutableException : DomainException
{
    /// <summary>Creates.</summary>
    public ActivityImmutableException() : base("ACTIVITY_IMMUTABLE", "An activity record is immutable and cannot be modified or deleted.") { }
}
#pragma warning restore CS1591
