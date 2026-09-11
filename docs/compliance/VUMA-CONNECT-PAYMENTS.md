# Vuma Connect payments

Stage 21b does not make Vuma a bank or payment institution. Card, EFT, instant-EFT and debit-order
funds must move through a licensed provider. Vuma receives provider tokens/references only; it never
stores PAN, CVV, online-banking credentials or provider secrets in the Connect domain.

The application boundary is `IPaymentGateway` for authorisation/capture and `ISettlementProvider` for
provider settlement. `IConnectLedgerPoster` is called only after successful settlement and is the
single place where the retailer AP and supplier AR facts are posted. A provider failure therefore
produces no ledger entries. Idempotency is keyed by the caller-supplied `IdempotencyKey` and the
payment identity; retries must return the original provider result.

`InMemoryConnectPaymentGateway` and `InMemoryConnectSettlementProvider` are non-money-moving fakes for
tests and local development. A production host must replace them with a licensed-provider adapter and
must fail closed when no adapter is configured.
