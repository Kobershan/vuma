namespace VumaRetail.Domain.CustomerAccounts;

/// <summary>What a member may do in a stokvel group.</summary>
public enum MemberRole
{
    /// <summary>Contributes and draws down. Sees only their own line by default.</summary>
    Member = 0,

    /// <summary>Chairs the group. Reads the group statement.</summary>
    Chairperson = 1,

    /// <summary>Keeps the books. Reads the group statement.</summary>
    Treasurer = 2,

    /// <summary>Keeps the minutes. Reads the group statement.</summary>
    Secretary = 3,
}
