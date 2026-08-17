namespace VumaRetail.Contracts.Procurement;

/// <summary>The id of something the <c>procurement</c> module just created.</summary>
/// <param name="Id">The new row's id.</param>
public sealed record ProcurementIdResponse(Guid Id);

/// <summary>Raises a purchase requisition.</summary>
/// <param name="RequiredBy">When the goods are needed by.</param>
/// <param name="Justification">Why. The thing an approver actually reads.</param>
/// <param name="LocationId">Where the goods are wanted, or <c>null</c>.</param>
public sealed record CreatePurchaseRequisitionRequest(
    DateOnly RequiredBy, string Justification, Guid? LocationId = null);

/// <summary>Puts a line on a draft requisition.</summary>
/// <param name="Description">What it is, in the requester's words.</param>
/// <param name="Quantity">How much is needed.</param>
/// <param name="UnitOfMeasure">The unit it is counted in.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="EstimatedUnitCost">What the requester thinks it costs, or <c>null</c>.</param>
/// <param name="Currency">The estimate's currency. Required when an estimate is supplied.</param>
public sealed record AddPurchaseRequisitionLineRequest(
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    Guid? ItemId = null,
    Guid? ItemVariantId = null,
    decimal? EstimatedUnitCost = null,
    string? Currency = null);

/// <summary>Approves or rejects a submitted requisition.</summary>
/// <param name="Approve">True to approve, false to reject.</param>
/// <param name="Reason">Why it was turned down. Required on a rejection.</param>
public sealed record DecidePurchaseRequisitionRequest(bool Approve, string? Reason = null);

/// <summary>One line of a requisition, as returned by the API.</summary>
/// <param name="Id">The line's id.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Description">What it is.</param>
/// <param name="Quantity">How much is needed.</param>
/// <param name="UnitOfMeasure">The unit it is counted in.</param>
/// <param name="EstimatedUnitCost">The estimate, or <c>null</c>.</param>
/// <param name="Currency">The estimate's currency, or <c>null</c>.</param>
/// <param name="SourcedToDocumentId">The RFQ or order that carries it, or <c>null</c>.</param>
public sealed record PurchaseRequisitionLineResponse(
    Guid Id,
    Guid? ItemId,
    Guid? ItemVariantId,
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal? EstimatedUnitCost,
    string? Currency,
    Guid? SourcedToDocumentId);

/// <summary>A requisition, as returned by the API.</summary>
/// <param name="Id">The requisition's id.</param>
/// <param name="RequisitionNumber">Its document number.</param>
/// <param name="RequestedByUserId">Who asked.</param>
/// <param name="LocationId">Where the goods are wanted, or <c>null</c>.</param>
/// <param name="RequiredBy">When they are needed by.</param>
/// <param name="Justification">Why.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="RaisedAt">When it was raised.</param>
/// <param name="DecidedByUserId">Who decided, or <c>null</c>.</param>
/// <param name="DecidedAt">When they decided, or <c>null</c>.</param>
/// <param name="RejectionReason">Why it was turned down, or <c>null</c>.</param>
/// <param name="Lines">What is being asked for.</param>
public sealed record PurchaseRequisitionResponse(
    Guid Id,
    string RequisitionNumber,
    Guid RequestedByUserId,
    Guid? LocationId,
    DateOnly RequiredBy,
    string Justification,
    string Status,
    DateTimeOffset RaisedAt,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAt,
    string? RejectionReason,
    IReadOnlyList<PurchaseRequisitionLineResponse> Lines);

/// <summary>Raises an RFQ.</summary>
/// <param name="Title">What is being sourced.</param>
/// <param name="ClosesAt">When quoting closes.</param>
/// <param name="PurchaseRequisitionId">The approved requisition it comes from, or <c>null</c>.</param>
public sealed record CreateRfqRequest(
    string Title, DateTimeOffset ClosesAt, Guid? PurchaseRequisitionId = null);

/// <summary>Puts a line on a draft RFQ.</summary>
/// <param name="Description">What is wanted.</param>
/// <param name="Quantity">How much.</param>
/// <param name="UnitOfMeasure">The unit it is counted in.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Specification">Any further requirement, or <c>null</c>.</param>
/// <param name="PurchaseRequisitionLineId">The requisition line this satisfies, or <c>null</c>.</param>
public sealed record AddRfqLineRequest(
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    Guid? ItemId = null,
    Guid? ItemVariantId = null,
    string? Specification = null,
    Guid? PurchaseRequisitionLineId = null);

/// <summary>Records one supplier's quote against an open RFQ.</summary>
/// <param name="PartnerId">The supplier.</param>
/// <param name="Currency">The currency they quoted in.</param>
/// <param name="QuotedAt">When the quote is dated.</param>
/// <param name="LeadTimeDays">How long they say delivery takes.</param>
/// <param name="ValidUntil">How long they hold the price, or <c>null</c>.</param>
/// <param name="Notes">Anything else they said.</param>
public sealed record RecordRfqResponseRequest(
    Guid PartnerId,
    string Currency,
    DateTimeOffset QuotedAt,
    int LeadTimeDays,
    DateTimeOffset? ValidUntil = null,
    string? Notes = null);

/// <summary>Puts a quoted price against one of the RFQ's lines.</summary>
/// <param name="RfqLineId">The line being quoted for.</param>
/// <param name="UnitCost">What the supplier charges per unit.</param>
/// <param name="Currency">The currency, which must be the response's.</param>
/// <param name="AvailableQuantity">How much they can supply, or <c>null</c> for all of it.</param>
public sealed record AddRfqResponseLineRequest(
    Guid RfqLineId,
    decimal UnitCost,
    string Currency,
    decimal? AvailableQuantity = null);

/// <summary>Awards the RFQ to one supplier's quote.</summary>
/// <param name="RfqResponseId">The winning response.</param>
public sealed record AwardRfqRequest(Guid RfqResponseId);

/// <summary>Closes an RFQ nobody won, or withdraws one.</summary>
/// <param name="Cancel">True to withdraw it, false to close it unawarded.</param>
public sealed record CloseRfqRequest(bool Cancel = false);

/// <summary>One quoted line, as returned by the API.</summary>
/// <param name="Id">The line's id.</param>
/// <param name="RfqLineId">The RFQ line quoted for.</param>
/// <param name="RequestedQuantity">How much was asked for.</param>
/// <param name="QuotedQuantity">How much they can supply.</param>
/// <param name="UnitCost">What they charge per unit.</param>
/// <param name="ExtendedCost">The quoted quantity at that cost.</param>
public sealed record RfqResponseLineResponse(
    Guid Id,
    Guid RfqLineId,
    decimal RequestedQuantity,
    decimal QuotedQuantity,
    decimal UnitCost,
    decimal ExtendedCost);

/// <summary>One supplier's quote, as returned by the API.</summary>
/// <param name="Id">The response's id.</param>
/// <param name="PartnerId">The supplier.</param>
/// <param name="Currency">The currency they quoted in.</param>
/// <param name="QuotedAt">When the quote is dated.</param>
/// <param name="ValidUntil">How long they hold it, or <c>null</c>.</param>
/// <param name="LeadTimeDays">How long delivery takes.</param>
/// <param name="Status">Submitted, Awarded or Declined.</param>
/// <param name="Total">The quote's total — what a buyer compares.</param>
/// <param name="Notes">Anything else they said.</param>
/// <param name="Lines">What they quoted, line by line.</param>
public sealed record RfqResponseResponse(
    Guid Id,
    Guid PartnerId,
    string Currency,
    DateTimeOffset QuotedAt,
    DateTimeOffset? ValidUntil,
    int LeadTimeDays,
    string Status,
    decimal Total,
    string? Notes,
    IReadOnlyList<RfqResponseLineResponse> Lines);

/// <summary>One RFQ line, as returned by the API.</summary>
/// <param name="Id">The line's id.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Description">What is wanted.</param>
/// <param name="Quantity">How much.</param>
/// <param name="UnitOfMeasure">The unit it is counted in.</param>
/// <param name="Specification">Any further requirement, or <c>null</c>.</param>
public sealed record RfqLineResponse(
    Guid Id,
    Guid? ItemId,
    Guid? ItemVariantId,
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    string? Specification);

/// <summary>An RFQ, as returned by the API.</summary>
/// <param name="Id">The RFQ's id.</param>
/// <param name="RfqNumber">Its document number.</param>
/// <param name="Title">What is being sourced.</param>
/// <param name="PurchaseRequisitionId">The requisition it came from, or <c>null</c>.</param>
/// <param name="ClosesAt">When quoting closes.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="AwardedResponseId">The quote that won, or <c>null</c>.</param>
/// <param name="Lines">What is being asked for.</param>
/// <param name="Responses">What the suppliers said.</param>
public sealed record RfqResponsePayload(
    Guid Id,
    string RfqNumber,
    string Title,
    Guid? PurchaseRequisitionId,
    DateTimeOffset ClosesAt,
    string Status,
    Guid? AwardedResponseId,
    IReadOnlyList<RfqLineResponse> Lines,
    IReadOnlyList<RfqResponseResponse> Responses);

/// <summary>Raises a purchase order.</summary>
/// <param name="PartnerId">The supplier.</param>
/// <param name="Currency">The currency, bound for the life of the document.</param>
/// <param name="LocationId">Where the goods are to be delivered.</param>
/// <param name="ExpectedAt">When they are expected.</param>
/// <param name="RfqResponseId">The quote this was awarded from, or <c>null</c>.</param>
/// <param name="Notes">Delivery instructions and terms, or <c>null</c>.</param>
public sealed record CreatePurchaseOrderRequest(
    Guid PartnerId,
    string Currency,
    Guid LocationId,
    DateOnly ExpectedAt,
    Guid? RfqResponseId = null,
    string? Notes = null);

/// <summary>Puts a line on a draft order.</summary>
/// <param name="Description">What is being bought.</param>
/// <param name="Quantity">How much.</param>
/// <param name="UnitOfMeasure">The unit it is counted in.</param>
/// <param name="UnitCost">What one costs.</param>
/// <param name="Currency">The currency, which must be the order's.</param>
/// <param name="TaxCode">The tax code the line is bought under.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="PurchaseRequisitionLineId">The requisition line this satisfies, or <c>null</c>.</param>
public sealed record AddPurchaseOrderLineRequest(
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitCost,
    string Currency,
    string TaxCode,
    Guid? ItemId = null,
    Guid? ItemVariantId = null,
    Guid? PurchaseRequisitionLineId = null);

/// <summary>Approves a draft order, and optionally sends it in the same act.</summary>
/// <param name="Issue">True to issue it to the supplier immediately after approving.</param>
public sealed record ApprovePurchaseOrderRequest(bool Issue = false);

/// <summary>Opens a replacement order and cancels the one it supersedes.</summary>
/// <param name="Reason">Why the original is being superseded.</param>
public sealed record AmendPurchaseOrderRequest(string Reason);

/// <summary>Closes or cancels an order.</summary>
/// <param name="Cancel">True to cancel it, false to close it.</param>
/// <param name="Reason">Why it was cancelled. Required on a cancellation.</param>
public sealed record ClosePurchaseOrderRequest(bool Cancel = false, string? Reason = null);

/// <summary>One order line, as returned by the API.</summary>
/// <param name="Id">The line's id.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Description">What is being bought.</param>
/// <param name="Quantity">How much was ordered.</param>
/// <param name="UnitOfMeasure">The unit it is counted in.</param>
/// <param name="UnitCost">What one costs.</param>
/// <param name="TaxCode">The tax code.</param>
/// <param name="Net">The line excluding tax.</param>
/// <param name="Tax">The tax on the line.</param>
/// <param name="Gross">What the line commits.</param>
/// <param name="ReceivedQuantity">How much has arrived and been accepted.</param>
/// <param name="RejectedQuantity">How much arrived and was turned away.</param>
/// <param name="InvoicedQuantity">How much released matches have claimed.</param>
/// <param name="OutstandingQuantity">How much is still to come.</param>
public sealed record PurchaseOrderLineResponse(
    Guid Id,
    Guid? ItemId,
    Guid? ItemVariantId,
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitCost,
    string TaxCode,
    decimal Net,
    decimal Tax,
    decimal Gross,
    decimal ReceivedQuantity,
    decimal RejectedQuantity,
    decimal InvoicedQuantity,
    decimal OutstandingQuantity);

/// <summary>A purchase order, as returned by the API.</summary>
/// <param name="Id">The order's id.</param>
/// <param name="OrderNumber">Its document number.</param>
/// <param name="PartnerId">The supplier.</param>
/// <param name="Currency">The currency the whole document is in.</param>
/// <param name="LocationId">Where the goods are to be delivered.</param>
/// <param name="ExpectedAt">When they are expected.</param>
/// <param name="Status">Where it stands.</param>
/// <param name="Version">1 for an original, incremented by each amendment.</param>
/// <param name="AmendsPurchaseOrderId">The order this one supersedes, or <c>null</c>.</param>
/// <param name="RfqResponseId">The quote it was awarded from, or <c>null</c>.</param>
/// <param name="Net">The order excluding tax.</param>
/// <param name="Tax">The tax on it.</param>
/// <param name="Gross">What it commits.</param>
/// <param name="Notes">Delivery instructions and terms.</param>
/// <param name="Lines">What was ordered.</param>
public sealed record PurchaseOrderResponse(
    Guid Id,
    string OrderNumber,
    Guid PartnerId,
    string Currency,
    Guid LocationId,
    DateOnly ExpectedAt,
    string Status,
    int Version,
    Guid? AmendsPurchaseOrderId,
    Guid? RfqResponseId,
    decimal Net,
    decimal Tax,
    decimal Gross,
    string? Notes,
    IReadOnlyList<PurchaseOrderLineResponse> Lines);

/// <summary>Opens a goods receipt against an issued order.</summary>
/// <param name="PurchaseOrderId">The order the goods came against.</param>
/// <param name="DeliveryNoteNumber">The supplier's own delivery-note number, or <c>null</c>.</param>
/// <param name="ReceivedAt">When the goods actually arrived, or <c>null</c> for now.</param>
public sealed record CreateGoodsReceiptRequest(
    Guid PurchaseOrderId, string? DeliveryNoteNumber = null, DateTimeOffset? ReceivedAt = null);

/// <summary>Records what arrived against one order line.</summary>
/// <param name="PurchaseOrderLineId">The order line.</param>
/// <param name="AcceptedQuantity">How much was accepted into stock.</param>
/// <param name="UnitOfMeasure">The unit it is counted in.</param>
/// <param name="RejectedQuantity">How much was turned away, or <c>null</c>.</param>
/// <param name="RejectionReason">Why — one of None, Damaged, Expired, WrongItem, QualityFailure, OverDelivery.</param>
/// <param name="Note">Anything the receiver wants on the record.</param>
public sealed record AddGoodsReceiptLineRequest(
    Guid PurchaseOrderLineId,
    decimal AcceptedQuantity,
    string UnitOfMeasure,
    decimal? RejectedQuantity = null,
    string RejectionReason = "None",
    string? Note = null);

/// <summary>One receipt line, as returned by the API.</summary>
/// <param name="Id">The line's id.</param>
/// <param name="PurchaseOrderLineId">The order line the goods came against.</param>
/// <param name="ItemId">The item, when it has no variants.</param>
/// <param name="ItemVariantId">The variant.</param>
/// <param name="Description">What the order called it.</param>
/// <param name="OrderedQuantity">How much the order line ordered.</param>
/// <param name="AcceptedQuantity">How much was accepted.</param>
/// <param name="RejectedQuantity">How much was turned away.</param>
/// <param name="RejectionReason">Why.</param>
/// <param name="UnitOfMeasure">The unit it is counted in.</param>
/// <param name="UnitCost">What the order says one costs.</param>
/// <param name="AcceptedValue">The accepted quantity at that cost.</param>
/// <param name="StockPosting">Pending, Posted or Refused.</param>
/// <param name="StockLedgerEntryId">The ledger row it produced, or <c>null</c>.</param>
/// <param name="StockPostingNote">Why the posting was refused, or <c>null</c>.</param>
public sealed record GoodsReceiptLineResponse(
    Guid Id,
    Guid PurchaseOrderLineId,
    Guid? ItemId,
    Guid? ItemVariantId,
    string Description,
    decimal OrderedQuantity,
    decimal AcceptedQuantity,
    decimal RejectedQuantity,
    string RejectionReason,
    string UnitOfMeasure,
    decimal UnitCost,
    decimal AcceptedValue,
    string StockPosting,
    Guid? StockLedgerEntryId,
    string? StockPostingNote);

/// <summary>A goods receipt, as returned by the API.</summary>
/// <param name="Id">The receipt's id.</param>
/// <param name="ReceiptNumber">Its document number.</param>
/// <param name="PurchaseOrderId">The order the goods came against.</param>
/// <param name="PartnerId">The supplier.</param>
/// <param name="LocationId">Where the goods landed.</param>
/// <param name="Currency">The currency.</param>
/// <param name="DeliveryNoteNumber">The supplier's delivery-note number, or <c>null</c>.</param>
/// <param name="ReceivedAt">When the goods arrived.</param>
/// <param name="Status">Where the receipt stands.</param>
/// <param name="ReceivedValue">The value of what was accepted, at order cost.</param>
/// <param name="StockPostingsRefused">How many lines the ledger refused (ADR-073).</param>
/// <param name="Lines">What arrived.</param>
public sealed record GoodsReceiptResponse(
    Guid Id,
    string ReceiptNumber,
    Guid PurchaseOrderId,
    Guid PartnerId,
    Guid LocationId,
    string Currency,
    string? DeliveryNoteNumber,
    DateTimeOffset ReceivedAt,
    string Status,
    decimal ReceivedValue,
    int StockPostingsRefused,
    IReadOnlyList<GoodsReceiptLineResponse> Lines);

/// <summary>What completing a receipt hands back.</summary>
/// <param name="GoodsReceiptId">The receipt.</param>
/// <param name="ReceiptNumber">Its document number.</param>
/// <param name="ReceivedValue">What was accepted, at order cost.</param>
/// <param name="Currency">The currency.</param>
/// <param name="OrderStatus">Where the order stands now.</param>
/// <param name="StockPostingsRefused">How many lines the ledger refused.</param>
public sealed record GoodsReceiptCompletionResponse(
    Guid GoodsReceiptId,
    string ReceiptNumber,
    decimal ReceivedValue,
    string Currency,
    string OrderStatus,
    int StockPostingsRefused);

/// <summary>One line off a supplier's invoice, as it is being keyed in.</summary>
/// <param name="Description">What the supplier called it.</param>
/// <param name="Quantity">How much they are claiming.</param>
/// <param name="UnitOfMeasure">The unit it is counted in.</param>
/// <param name="UnitCost">What they are charging per unit.</param>
/// <param name="PurchaseOrderLineId">
/// The order line they say it is for, or <c>null</c> when it is not on the order at all — which blocks
/// the match rather than being refused (ADR-082).
/// </param>
public sealed record SupplierInvoiceLineRequest(
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitCost,
    Guid? PurchaseOrderLineId = null);

/// <summary>Runs a three-way match of a supplier's invoice against an order and its receipts.</summary>
/// <param name="SupplierInvoiceNumber">The supplier's own invoice number.</param>
/// <param name="InvoiceDate">The date on their invoice.</param>
/// <param name="ClaimedNet">What they are claiming, excluding tax.</param>
/// <param name="ClaimedTax">The tax they are claiming.</param>
/// <param name="Currency">The currency, which must be the order's.</param>
/// <param name="Lines">Their invoice's lines.</param>
public sealed record MatchSupplierInvoiceRequest(
    string SupplierInvoiceNumber,
    DateOnly InvoiceDate,
    decimal ClaimedNet,
    decimal ClaimedTax,
    string Currency,
    IReadOnlyList<SupplierInvoiceLineRequest> Lines);

/// <summary>One line of the comparison, as returned by the API.</summary>
/// <param name="Id">The line's id.</param>
/// <param name="PurchaseOrderLineId">The order line, or <c>null</c> for a line not on the order.</param>
/// <param name="Description">What the line is.</param>
/// <param name="OrderedQuantity">How much the order committed to.</param>
/// <param name="ReceivedQuantity">How much has actually arrived.</param>
/// <param name="InvoicedQuantity">How much this invoice claims.</param>
/// <param name="PreviouslyInvoicedQuantity">How much earlier released matches claimed.</param>
/// <param name="UnitOfMeasure">The unit it is counted in.</param>
/// <param name="OrderedUnitCost">What the order says one costs.</param>
/// <param name="InvoicedUnitCost">What the supplier is charging.</param>
/// <param name="OrderedValue">What the order supports paying.</param>
/// <param name="InvoicedValue">What they are asking for.</param>
/// <param name="PriceVariance">The difference. Positive means overcharged.</param>
/// <param name="QuantityVariance">Cumulative invoiced less received. Positive means billed ahead of delivery.</param>
/// <param name="Status">This line's verdict.</param>
/// <param name="Variances">Which comparisons it failed.</param>
public sealed record SupplierInvoiceMatchLineResponse(
    Guid Id,
    Guid? PurchaseOrderLineId,
    string Description,
    decimal OrderedQuantity,
    decimal ReceivedQuantity,
    decimal InvoicedQuantity,
    decimal PreviouslyInvoicedQuantity,
    string UnitOfMeasure,
    decimal OrderedUnitCost,
    decimal InvoicedUnitCost,
    decimal OrderedValue,
    decimal InvoicedValue,
    decimal PriceVariance,
    decimal QuantityVariance,
    string Status,
    string Variances);

/// <summary>A three-way match, as returned by the API.</summary>
/// <param name="Id">The match's id.</param>
/// <param name="PurchaseOrderId">The order matched against.</param>
/// <param name="PartnerId">The supplier.</param>
/// <param name="SupplierInvoiceNumber">Their invoice number.</param>
/// <param name="InvoiceDate">The date on it.</param>
/// <param name="Currency">The currency.</param>
/// <param name="ClaimedNet">What they are claiming, excluding tax.</param>
/// <param name="ClaimedTax">The tax claimed.</param>
/// <param name="ClaimedGross">What they want paid.</param>
/// <param name="MatchedNet">What the order supports.</param>
/// <param name="PriceVariance">The difference.</param>
/// <param name="Status">The verdict.</param>
/// <param name="Variances">Which comparisons failed.</param>
/// <param name="IsPayable">True when the verdict permits a release.</param>
/// <param name="ReleasedAt">When it was released, or <c>null</c>.</param>
/// <param name="JournalId">The GL journal the release posted, or <c>null</c>.</param>
/// <param name="Lines">The comparison, line by line.</param>
public sealed record SupplierInvoiceMatchResponse(
    Guid Id,
    Guid PurchaseOrderId,
    Guid PartnerId,
    string SupplierInvoiceNumber,
    DateOnly InvoiceDate,
    string Currency,
    decimal ClaimedNet,
    decimal ClaimedTax,
    decimal ClaimedGross,
    decimal MatchedNet,
    decimal PriceVariance,
    string Status,
    string Variances,
    bool IsPayable,
    DateTimeOffset? ReleasedAt,
    Guid? JournalId,
    IReadOnlyList<SupplierInvoiceMatchLineResponse> Lines);

/// <summary>What running a match hands back.</summary>
/// <param name="SupplierInvoiceMatchId">The match document, whatever its verdict.</param>
/// <param name="Status">The verdict.</param>
/// <param name="Variances">Which comparisons failed.</param>
/// <param name="ClaimedNet">What the supplier is claiming.</param>
/// <param name="MatchedNet">What the order supports.</param>
/// <param name="PriceVariance">The difference.</param>
/// <param name="Currency">The currency.</param>
/// <param name="IsPayable">True when the verdict permits a release.</param>
public sealed record ThreeWayMatchResponse(
    Guid SupplierInvoiceMatchId,
    string Status,
    string Variances,
    decimal ClaimedNet,
    decimal MatchedNet,
    decimal PriceVariance,
    string Currency,
    bool IsPayable);

/// <summary>What releasing a match hands back.</summary>
/// <param name="SupplierInvoiceMatchId">The match.</param>
/// <param name="SupplierInvoiceNumber">The supplier's invoice number.</param>
/// <param name="Gross">What the supplier will be paid.</param>
/// <param name="Currency">The currency.</param>
/// <param name="JournalId">The GL journal that was posted, or <c>null</c>.</param>
/// <param name="OrderStatus">Where the order stands now.</param>
public sealed record SupplierInvoiceReleaseResponse(
    Guid SupplierInvoiceMatchId,
    string SupplierInvoiceNumber,
    decimal Gross,
    string Currency,
    Guid? JournalId,
    string OrderStatus);

/// <summary>Takes a supplier scorecard snapshot for one closed period.</summary>
/// <param name="PartnerId">The supplier.</param>
/// <param name="PeriodStart">The period's first day, inclusive.</param>
/// <param name="PeriodEnd">The period's last day, inclusive.</param>
/// <param name="Currency">The currency to report the money figures in.</param>
public sealed record SnapshotSupplierScorecardRequest(
    Guid PartnerId, DateOnly PeriodStart, DateOnly PeriodEnd, string Currency);

/// <summary>A supplier scorecard, as returned by the API.</summary>
/// <param name="Id">The scorecard's id.</param>
/// <param name="PartnerId">The supplier.</param>
/// <param name="PeriodStart">The period's first day.</param>
/// <param name="PeriodEnd">The period's last day.</param>
/// <param name="Currency">The currency the money figures are in.</param>
/// <param name="OrdersPlaced">Orders issued in the period.</param>
/// <param name="LinesOrdered">Order lines those orders carried.</param>
/// <param name="LinesDelivered">Lines with anything delivered.</param>
/// <param name="LinesDeliveredOnTime">Lines delivered on or before the expected date.</param>
/// <param name="LinesWithRejections">Deliveries with something turned away.</param>
/// <param name="QuantityOrdered">Quantity ordered, across units of measure.</param>
/// <param name="QuantityReceived">Quantity accepted.</param>
/// <param name="QuantityRejected">Quantity turned away.</param>
/// <param name="PurchaseValue">What was bought, at order cost, excluding tax.</param>
/// <param name="PriceVariance">What released invoices over-billed by, net.</param>
/// <param name="OnTimeDeliveryRate">On-time lines as a percentage of delivered lines.</param>
/// <param name="FillRate">Received quantity as a percentage of ordered.</param>
/// <param name="QualityRate">Accepted quantity as a percentage of everything that arrived.</param>
/// <param name="OverallRating">The plain average of the three rates.</param>
/// <param name="SnapshottedAt">When the snapshot was taken.</param>
public sealed record SupplierScorecardResponse(
    Guid Id,
    Guid PartnerId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Currency,
    int OrdersPlaced,
    int LinesOrdered,
    int LinesDelivered,
    int LinesDeliveredOnTime,
    int LinesWithRejections,
    decimal QuantityOrdered,
    decimal QuantityReceived,
    decimal QuantityRejected,
    decimal PurchaseValue,
    decimal PriceVariance,
    decimal OnTimeDeliveryRate,
    decimal FillRate,
    decimal QualityRate,
    decimal OverallRating,
    DateTimeOffset SnapshottedAt);
