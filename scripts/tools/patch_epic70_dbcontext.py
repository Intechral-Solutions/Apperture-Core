from pathlib import Path
p = Path(r"f:/Code/Apperture/Apperture-Core/src/Apperture.Persistence/Apperture.Persistence.SqlServer/AppertureDbContext.cs")
t = p.read_text(encoding="utf-8")
old = """            e.Property(x => x.SubjectSnapshot).HasMaxLength(512);
            e.HasIndex(x => new { x.AttentionIncidentId, x.NotificationKind });
            e.HasIndex(x => new { x.AttentionIncidentId, x.CreatedAtUtc });
        });
    }
}"""
new = """            e.Property(x => x.SubjectSnapshot).HasMaxLength(512);
            e.Property(x => x.RecipientResolvedFrom).HasMaxLength(64);
            e.Property(x => x.WasEscalation);
            e.Property(x => x.EscalationThresholdApplied);
            e.Property(x => x.SuppressedDueToAcknowledgment);
            e.HasIndex(x => new { x.AttentionIncidentId, x.NotificationKind });
            e.HasIndex(x => new { x.AttentionIncidentId, x.CreatedAtUtc });
        });

        modelBuilder.Entity<InstanceAttentionRoutingEntity>(e =>
        {
            e.ToTable("InstanceAttentionRoutings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.ConditionKey).HasMaxLength(128);
            e.HasIndex(x => x.ConditionKey);
        });

        modelBuilder.Entity<IntegrationAttentionRoutingEntity>(e =>
        {
            e.ToTable("IntegrationAttentionRoutings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.IntegrationId).HasMaxLength(64);
            e.Property(x => x.ConditionKey).HasMaxLength(128);
            e.HasIndex(x => new { x.IntegrationId, x.ConditionKey }).IsUnique();
        });

        modelBuilder.Entity<AttentionRoutingRecipientEntity>(e =>
        {
            e.ToTable("AttentionRoutingRecipients");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.InstanceAttentionRoutingId).HasMaxLength(64);
            e.Property(x => x.IntegrationAttentionRoutingId).HasMaxLength(64);
            e.Property(x => x.Role).HasMaxLength(32);
            e.Property(x => x.Address).HasMaxLength(512);
            e.HasIndex(x => x.InstanceAttentionRoutingId);
            e.HasIndex(x => x.IntegrationAttentionRoutingId);
        });
    }
}"""
assert old in t
p.write_text(t.replace(old, new, 1), encoding="utf-8")
print("ok")
