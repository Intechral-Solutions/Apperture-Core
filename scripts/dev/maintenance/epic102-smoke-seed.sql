SET QUOTED_IDENTIFIER ON;
GO

UPDATE NotificationInstances
SET RecipientSnapshotJson = N'{"deliverable":[{"email":"tennyson.hull@gmail.com","displayName":"Test User","userId":"49955a58e84244a28af8341b","recipientKind":"principal","source":"directSubscription"}],"skipped":[]}'
WHERE Id = N'ninst_bcecc254bc8249f392333017381fde46';
GO

DELETE FROM NotificationUserInboxItems;
GO

INSERT INTO NotificationUserInboxItems (
    Id, NotificationInstanceId, PrincipalUserId, SourceRecipientKind, SourceRecipientRef, IsRead, CreatedAtUtc)
VALUES (
    N'ninbox_smoke_other',
    N'ninst_bcecc254bc8249f392333017381fde46',
    N'other-user-id',
    N'principal',
    N'other-user-id',
    0,
    SYSUTCDATETIME());
GO
