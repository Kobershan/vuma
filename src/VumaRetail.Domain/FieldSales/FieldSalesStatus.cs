namespace VumaRetail.Domain.FieldSales;

/// <summary>Status of a pro forma order or credit note (Stage 14b, ADR-107).</summary>
public enum ProFormaStatus
{
    /// <summary>Being captured by the rep. Editable, invisible to approvers.</summary>
    Draft = 0,

    /// <summary>Sent for approval. An approval request exists; the document is frozen.</summary>
    Submitted = 1,

    /// <summary>Approved by management. May proceed to conversion.</summary>
    Approved = 2,

    /// <summary>Returned to the rep with a reason. Editable again once amended.</summary>
    Amended = 3,

    /// <summary>Refused with a reason. Terminal; nothing was reserved or posted.</summary>
    Rejected = 4,

    /// <summary>Past its expiry unapproved. Must be re-priced before approval.</summary>
    Expired = 5,

    /// <summary>Converted into a real order (or applied as a sales return). Terminal.</summary>
    Converted = 6,

    /// <summary>Withdrawn by the rep before a decision. Terminal.</summary>
    Withdrawn = 7,
}
