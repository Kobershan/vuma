using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VumaRetail.Application.Abstractions;
using VumaRetail.Application.Abstractions.Workflow;
using VumaRetail.Domain.Workflow;
using VumaRetail.IntegrationTests.Api;
using VumaRetail.IntegrationTests.Harness;
using VumaRetail.Workflow.Notifications;

namespace VumaRetail.IntegrationTests.Workflow;

/// <summary>
/// Notification dispatch end to end: a readable in-app row, and the log channel called for
/// email/push (<c>docs/stages/STAGE-05-workflow.md</c> "Tests / acceptance").
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class NotificationDispatchTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Dispatching_on_every_channel_creates_one_row_per_channel_and_marks_each_sent()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        Guid recipientId = await harness.CreateUserAsync("recipient", permissions: []);

        IReadOnlyList<Guid> created = await harness.InScopeAsync(async provider =>
        {
            INotificationDispatcher dispatcher = provider.GetRequiredService<INotificationDispatcher>();

            IReadOnlyList<Guid> notificationIds = await dispatcher.NotifyAsync(new NotificationRequest(
                recipientId,
                "workflow.approval.pending",
                "An approval is waiting",
                "Purchase order PO-1042 needs your decision.",
                NotificationSeverity.Info,
                [NotificationChannel.InApp, NotificationChannel.Email, NotificationChannel.Push],
                "procurement",
                "purchase-order",
                Guid.NewGuid(),
                "/approvals/PO-1042"));

            await provider.GetRequiredService<IUnitOfWork>().CommitAsync();

            return notificationIds;
        });

        created.Should().HaveCount(3, "one row per requested channel");

        await harness.InScopeAsync(provider =>
        {
            VumaRetailDbContext context = provider.GetRequiredService<VumaRetailDbContext>();

            List<Notification> rows = context.Notifications
                .Where(notification => created.Contains(notification.Id))
                .ToList();

            rows.Should().HaveCount(3);
            rows.Should().OnlyContain(row => row.DeliveryStatus == NotificationDeliveryStatus.Sent);
            rows.Select(row => row.Channel).Should()
                .BeEquivalentTo([NotificationChannel.InApp, NotificationChannel.Email, NotificationChannel.Push]);

            // In-app delivery is real and persisted; email and push are the LogNotificationChannel
            // stand-in (docs/PROGRESS.md §3) — both still mark Sent, because logging what would have
            // been sent is a real, observable outcome rather than a silent no-op.
            return Task.FromResult(true);
        });
    }

    [Fact]
    public async Task A_recipients_own_inbox_lists_their_in_app_notifications_and_the_unread_count_matches()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        Guid recipientId = await harness.CreateUserAsync("recipient", permissions: []);

        await harness.InScopeAsync(async provider =>
        {
            INotificationDispatcher dispatcher = provider.GetRequiredService<INotificationDispatcher>();

            await dispatcher.NotifyAsync(new NotificationRequest(
                recipientId, "workflow.approval.pending", "First", "Body one",
                NotificationSeverity.Info, [NotificationChannel.InApp]));

            await dispatcher.NotifyAsync(new NotificationRequest(
                recipientId, "workflow.approval.pending", "Second", "Body two",
                NotificationSeverity.Warning, [NotificationChannel.InApp]));

            await provider.GetRequiredService<IUnitOfWork>().CommitAsync();

            return true;
        });

        HttpClient recipient = await harness.SignInAsync("recipient");

        int? unread = await recipient.GetFromJsonAsync<int?>("/api/v1/workflow/notifications/unread-count");
        unread.Should().Be(2);

        HttpResponseMessage listResponse = await recipient.GetAsync("/api/v1/workflow/notifications");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        System.Text.Json.JsonDocument list = System.Text.Json.JsonDocument.Parse(
            await listResponse.Content.ReadAsStringAsync());
        list.RootElement.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Marking_a_notification_read_updates_the_unread_count_and_is_idempotent()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        Guid recipientId = await harness.CreateUserAsync("recipient", permissions: []);

        Guid notificationId = await harness.InScopeAsync(async provider =>
        {
            INotificationDispatcher dispatcher = provider.GetRequiredService<INotificationDispatcher>();

            IReadOnlyList<Guid> created = await dispatcher.NotifyAsync(new NotificationRequest(
                recipientId, "workflow.approval.pending", "Waiting", "Please decide",
                NotificationSeverity.Info, [NotificationChannel.InApp]));

            await provider.GetRequiredService<IUnitOfWork>().CommitAsync();

            return created[0];
        });

        HttpClient recipient = await harness.SignInAsync("recipient");

        (await recipient.PostAsync($"/api/v1/workflow/notifications/{notificationId}/read", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Idempotent — marking an already-read notification read again does not error.
        (await recipient.PostAsync($"/api/v1/workflow/notifications/{notificationId}/read", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        int? unread = await recipient.GetFromJsonAsync<int?>("/api/v1/workflow/notifications/unread-count");
        unread.Should().Be(0);
    }

    [Fact]
    public async Task A_user_cannot_mark_somebody_elses_notification_read()
    {
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        Guid recipientId = await harness.CreateUserAsync("recipient", permissions: []);
        await harness.CreateUserAsync("intruder", permissions: []);

        Guid notificationId = await harness.InScopeAsync(async provider =>
        {
            INotificationDispatcher dispatcher = provider.GetRequiredService<INotificationDispatcher>();

            IReadOnlyList<Guid> created = await dispatcher.NotifyAsync(new NotificationRequest(
                recipientId, "workflow.approval.pending", "Private", "Not yours",
                NotificationSeverity.Info, [NotificationChannel.InApp]));

            await provider.GetRequiredService<IUnitOfWork>().CommitAsync();

            return created[0];
        });

        HttpClient intruder = await harness.SignInAsync("intruder");

        HttpResponseMessage response = await intruder.PostAsync($"/api/v1/workflow/notifications/{notificationId}/read", null);

        // Answered identically to "not found", the same way a cross-tenant lookup is
        // (docs/API_STANDARDS.md §4) — distinguishing "exists but is not yours" from "does not exist"
        // would make every id an existence oracle across other people's notifications.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_notification_missing_its_channel_registration_is_recorded_failed_not_silently_dropped()
    {
        // NotificationDispatcher's own defensive path — every registered channel is wired by
        // AddVumaWorkflow, so this exercises the "no channel found" branch directly rather than by
        // engineering a missing DI registration on the running host.
        await using ApiHarness harness = await ApiHarness.CreateAsync(fixture);
        Guid recipientId = await harness.CreateUserAsync("recipient", permissions: []);

        await harness.InScopeAsync(async provider =>
        {
            INotificationRepository repository = provider.GetRequiredService<INotificationRepository>();
            IClock clock = provider.GetRequiredService<IClock>();
            ITenantContext tenant = provider.GetRequiredService<ITenantContext>();

            NotificationDispatcher dispatcherWithNoChannels = new(
                repository, [], tenant, clock);

            await dispatcherWithNoChannels.NotifyAsync(new NotificationRequest(
                recipientId, "workflow.approval.pending", "Orphan", "No channel registered",
                NotificationSeverity.Info, [NotificationChannel.InApp]));

            await provider.GetRequiredService<IUnitOfWork>().CommitAsync();

            return true;
        });

        await harness.InScopeAsync(async provider =>
        {
            VumaRetailDbContext context = provider.GetRequiredService<VumaRetailDbContext>();

            Notification row = await context.Notifications
                .SingleAsync(notification => notification.RecipientUserId == recipientId
                    && notification.Category == "workflow.approval.pending");

            row.DeliveryStatus.Should().Be(NotificationDeliveryStatus.Failed);
            row.DeliveryError.Should().NotBeNullOrWhiteSpace();

            return true;
        });
    }
}
