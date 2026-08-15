namespace Archy.Features.Storage.WorkspaceDatabase.Initialize;

internal static class WorkspaceDatabaseSchema
{
    internal static IReadOnlyList<WorkspaceDatabaseMigration> Migrations { get; } =
    [
        WorkspaceDatabaseMigration.Create(1, "create_repository_and_analysis_run_tables", MigrationOneSql),
        WorkspaceDatabaseMigration.Create(2, "create_analysis_run_events", MigrationTwoSql),
        WorkspaceDatabaseMigration.Create(3, "create_graph_revision_snapshot_tables", MigrationThreeSql),
        WorkspaceDatabaseMigration.Create(4, "add_migration_audit_metadata", MigrationFourSql),
        WorkspaceDatabaseMigration.Create(5, "create_versioned_graph_facts", MigrationFiveSql),
        WorkspaceDatabaseMigration.Create(6, "create_versioned_symbols_and_interface_fingerprints", MigrationSixSql),
        WorkspaceDatabaseMigration.Create(7, "create_append_only_summary_versions", MigrationSevenSql),
        WorkspaceDatabaseMigration.Create(8, "create_append_only_decisions_sessions_and_events", MigrationEightSql),
        WorkspaceDatabaseMigration.Create(9, "create_duplicate_cluster_and_health_history", MigrationNineSql),
        WorkspaceDatabaseMigration.Create(10, "add_active_graph_traversal_and_replay_indexes", MigrationTenSql),
        WorkspaceDatabaseMigration.Create(11, "create_explicit_active_graph_revision_state", MigrationElevenSql),
        WorkspaceDatabaseMigration.Create(12, "create_source_inventory_hash_cache", MigrationTwelveSql),
        WorkspaceDatabaseMigration.Create(13, "create_analysis_run_repository_provenance", MigrationThirteenSql),
        WorkspaceDatabaseMigration.Create(14, "create_immutable_summary_batches", MigrationFourteenSql),
        WorkspaceDatabaseMigration.Create(15, "create_model_failure_work_queue", MigrationFifteenSql),
        WorkspaceDatabaseMigration.Create(16, "create_revision_aware_embedding_cache", MigrationSixteenSql),
        WorkspaceDatabaseMigration.Create(17, "create_duplicate_finding_lifecycle_history", MigrationSeventeenSql),
        WorkspaceDatabaseMigration.Create(18, "create_revisioned_similarity_clusters", MigrationEighteenSql),
        WorkspaceDatabaseMigration.Create(19, "index_revision_scoped_session_preflight_context", MigrationNineteenSql),
    ];

    internal const string CreateLegacyMigrationHistorySql = """
        CREATE TABLE schema_migrations (
            version INTEGER PRIMARY KEY,
            applied_at_utc TEXT NOT NULL
        );
        """;

    internal const string RepositoryUpsertSql = """
        INSERT INTO repositories(workspace_id, repository_root, configuration_hash, updated_at_utc)
        VALUES ($workspaceId, $repositoryRoot, $configurationHash, $updatedAt)
        ON CONFLICT(workspace_id) DO UPDATE SET
            repository_root = excluded.repository_root,
            configuration_hash = excluded.configuration_hash,
            updated_at_utc = excluded.updated_at_utc;
        """;

    internal const string WorkspaceGraphStateEnsureSql = """
        INSERT INTO workspace_graph_states(workspace_id, active_graph_revision, activated_at_utc)
        VALUES ($workspaceId, NULL, NULL)
        ON CONFLICT(workspace_id) DO NOTHING;
        """;

    private const string MigrationOneSql = """
        CREATE TABLE repositories (workspace_id TEXT PRIMARY KEY, repository_root TEXT NOT NULL, configuration_hash TEXT NOT NULL, updated_at_utc TEXT NOT NULL);
        CREATE TABLE analysis_runs (run_id TEXT PRIMARY KEY, workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id), analyzer_version TEXT NOT NULL, configuration_hash TEXT NOT NULL, repository_commit TEXT NULL, status TEXT NOT NULL, started_at_utc TEXT NOT NULL, completed_at_utc TEXT NULL, graph_revision INTEGER NULL);
        CREATE INDEX ix_analysis_runs_workspace_started ON analysis_runs(workspace_id, started_at_utc DESC);
        """;

    private const string MigrationSixteenSql = """
        CREATE TABLE embedding_cache_entries (
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            method_stable_id TEXT NOT NULL,
            model_id TEXT NOT NULL,
            content_hash TEXT NOT NULL,
            vector_json TEXT NOT NULL,
            vector_dimensions INTEGER NOT NULL CHECK(vector_dimensions > 0),
            created_graph_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            created_at_utc TEXT NOT NULL,
            PRIMARY KEY(workspace_id, method_stable_id, model_id, content_hash)
        );
        CREATE INDEX ix_embedding_cache_lookup
            ON embedding_cache_entries(workspace_id, method_stable_id, model_id, content_hash);
        """;

    private const string MigrationNineteenSql = """
        CREATE INDEX ix_session_events_preflight_revision
            ON session_events(workspace_id, session_id, event_type, graph_revision, sequence_number DESC);
        """;

    private const string MigrationSeventeenSql = """
        CREATE TABLE duplicate_finding_lifecycle_events (
            lifecycle_event_id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            finding_id TEXT NOT NULL REFERENCES duplicate_finding_identities(finding_id),
            graph_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            state TEXT NOT NULL CHECK(state IN ('active', 'closed', 'superseded')),
            superseded_by_finding_id TEXT NULL REFERENCES duplicate_finding_identities(finding_id),
            reason TEXT NOT NULL,
            occurred_at_utc TEXT NOT NULL,
            UNIQUE(workspace_id, finding_id, graph_revision)
        );
        CREATE INDEX ix_duplicate_finding_lifecycle_active
            ON duplicate_finding_lifecycle_events(workspace_id, graph_revision, finding_id);
        """;

    private const string MigrationEighteenSql = """
        CREATE TABLE similarity_cluster_revisions (
            similarity_cluster_revision_id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            graph_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            algorithm TEXT NOT NULL,
            algorithm_version TEXT NOT NULL,
            input_hash TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            UNIQUE(workspace_id, graph_revision, algorithm, algorithm_version, input_hash)
        );
        CREATE TABLE similarity_cluster_identities (
            similarity_cluster_id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            label TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            UNIQUE(workspace_id, label)
        );
        CREATE TABLE similarity_cluster_members (
            similarity_cluster_revision_id TEXT NOT NULL REFERENCES similarity_cluster_revisions(similarity_cluster_revision_id),
            similarity_cluster_id TEXT NOT NULL REFERENCES similarity_cluster_identities(similarity_cluster_id),
            member_stable_id TEXT NOT NULL,
            membership_score REAL NOT NULL CHECK(membership_score >= 0 AND membership_score <= 1),
            evidence_summary_json TEXT NOT NULL,
            member_ordinal INTEGER NOT NULL,
            PRIMARY KEY(similarity_cluster_revision_id, similarity_cluster_id, member_stable_id)
        );
        CREATE INDEX ix_similarity_cluster_revisions_workspace_graph ON similarity_cluster_revisions(workspace_id, graph_revision, created_at_utc DESC);
        CREATE INDEX ix_similarity_cluster_members_revision_cluster ON similarity_cluster_members(similarity_cluster_revision_id, similarity_cluster_id, member_ordinal);
        """;

    private const string MigrationTwoSql = """
        CREATE TABLE analysis_run_events (event_id INTEGER PRIMARY KEY AUTOINCREMENT, run_id TEXT NOT NULL REFERENCES analysis_runs(run_id), event_type TEXT NOT NULL, occurred_at_utc TEXT NOT NULL, graph_revision INTEGER NULL);
        CREATE INDEX ix_analysis_run_events_run ON analysis_run_events(run_id, event_id);
        """;

    private const string MigrationThreeSql = """
        CREATE TABLE graph_revisions (revision INTEGER PRIMARY KEY AUTOINCREMENT, workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id), run_id TEXT NOT NULL UNIQUE REFERENCES analysis_runs(run_id), committed_at_utc TEXT NOT NULL);
        CREATE TABLE graph_nodes (revision INTEGER NOT NULL REFERENCES graph_revisions(revision), stable_id TEXT NOT NULL, node_kind TEXT NOT NULL, canonical_key TEXT NOT NULL, display_name TEXT NOT NULL, file_path TEXT NULL, start_line INTEGER NULL, end_line INTEGER NULL, provider TEXT NOT NULL, confidence REAL NOT NULL, evidence_json TEXT NOT NULL, PRIMARY KEY(revision, stable_id));
        CREATE TABLE graph_edges (revision INTEGER NOT NULL REFERENCES graph_revisions(revision), edge_id TEXT NOT NULL, source_stable_id TEXT NOT NULL, target_stable_id TEXT NOT NULL, edge_kind TEXT NOT NULL, normalized_join_key TEXT NULL, provider TEXT NOT NULL, confidence REAL NOT NULL, evidence_json TEXT NOT NULL, PRIMARY KEY(revision, edge_id));
        CREATE INDEX ix_graph_nodes_canonical ON graph_nodes(revision, canonical_key);
        CREATE INDEX ix_graph_edges_source ON graph_edges(revision, source_stable_id);
        CREATE INDEX ix_graph_edges_target ON graph_edges(revision, target_stable_id);
        """;

    private const string MigrationFourSql = """
        ALTER TABLE schema_migrations ADD COLUMN name TEXT NULL;
        ALTER TABLE schema_migrations ADD COLUMN checksum TEXT NULL;
        """;

    private const string MigrationFiveSql = """
        ALTER TABLE graph_nodes ADD COLUMN content_hash TEXT NULL;

        CREATE TABLE graph_node_identities (
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            stable_id TEXT NOT NULL,
            first_seen_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            last_seen_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            PRIMARY KEY(workspace_id, stable_id)
        );

        CREATE TABLE graph_node_versions (
            workspace_id TEXT NOT NULL,
            stable_id TEXT NOT NULL,
            valid_from_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            valid_to_revision INTEGER NULL REFERENCES graph_revisions(revision),
            producing_run_id TEXT NOT NULL REFERENCES analysis_runs(run_id),
            node_kind TEXT NOT NULL,
            canonical_key TEXT NOT NULL,
            display_name TEXT NOT NULL,
            file_path TEXT NULL,
            start_line INTEGER NULL,
            end_line INTEGER NULL,
            provider TEXT NOT NULL,
            confidence REAL NOT NULL,
            evidence_json TEXT NOT NULL,
            content_hash TEXT NOT NULL,
            PRIMARY KEY(workspace_id, stable_id, valid_from_revision),
            FOREIGN KEY(workspace_id, stable_id) REFERENCES graph_node_identities(workspace_id, stable_id),
            CHECK(valid_to_revision IS NULL OR valid_to_revision >= valid_from_revision)
        );

        CREATE TABLE graph_edge_identities (
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            edge_id TEXT NOT NULL,
            source_stable_id TEXT NOT NULL,
            target_stable_id TEXT NOT NULL,
            edge_kind TEXT NOT NULL,
            normalized_join_key TEXT NULL,
            first_seen_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            last_seen_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            PRIMARY KEY(workspace_id, edge_id)
        );

        CREATE TABLE graph_edge_versions (
            workspace_id TEXT NOT NULL,
            edge_id TEXT NOT NULL,
            valid_from_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            valid_to_revision INTEGER NULL REFERENCES graph_revisions(revision),
            producing_run_id TEXT NOT NULL REFERENCES analysis_runs(run_id),
            source_stable_id TEXT NOT NULL,
            target_stable_id TEXT NOT NULL,
            edge_kind TEXT NOT NULL,
            normalized_join_key TEXT NULL,
            provider TEXT NOT NULL,
            confidence REAL NOT NULL,
            evidence_json TEXT NOT NULL,
            PRIMARY KEY(workspace_id, edge_id, valid_from_revision),
            FOREIGN KEY(workspace_id, edge_id) REFERENCES graph_edge_identities(workspace_id, edge_id),
            CHECK(valid_to_revision IS NULL OR valid_to_revision >= valid_from_revision)
        );

        INSERT INTO graph_node_identities(workspace_id, stable_id, first_seen_revision, last_seen_revision)
        SELECT graph_revisions.workspace_id, graph_nodes.stable_id, MIN(graph_nodes.revision), MAX(graph_nodes.revision)
        FROM graph_nodes
        INNER JOIN graph_revisions ON graph_revisions.revision = graph_nodes.revision
        GROUP BY graph_revisions.workspace_id, graph_nodes.stable_id;

        INSERT INTO graph_node_versions(workspace_id, stable_id, valid_from_revision, valid_to_revision, producing_run_id, node_kind, canonical_key, display_name, file_path, start_line, end_line, provider, confidence, evidence_json, content_hash)
        SELECT
            graph_revisions.workspace_id,
            graph_nodes.stable_id,
            graph_nodes.revision,
            CASE WHEN graph_nodes.revision = MAX(graph_nodes.revision) OVER (PARTITION BY graph_revisions.workspace_id, graph_nodes.stable_id) THEN NULL ELSE graph_nodes.revision END,
            graph_revisions.run_id,
            graph_nodes.node_kind,
            graph_nodes.canonical_key,
            graph_nodes.display_name,
            graph_nodes.file_path,
            graph_nodes.start_line,
            graph_nodes.end_line,
            graph_nodes.provider,
            graph_nodes.confidence,
            graph_nodes.evidence_json,
            COALESCE(graph_nodes.content_hash, 'legacy:' || graph_nodes.revision || ':' || graph_nodes.stable_id)
        FROM graph_nodes
        INNER JOIN graph_revisions ON graph_revisions.revision = graph_nodes.revision;

        WITH ranked_edges AS (
            SELECT
                graph_revisions.workspace_id,
                graph_edges.edge_id,
                graph_edges.source_stable_id,
                graph_edges.target_stable_id,
                graph_edges.edge_kind,
                graph_edges.normalized_join_key,
                MIN(graph_edges.revision) OVER (PARTITION BY graph_revisions.workspace_id, graph_edges.edge_id) AS first_seen_revision,
                MAX(graph_edges.revision) OVER (PARTITION BY graph_revisions.workspace_id, graph_edges.edge_id) AS last_seen_revision,
                ROW_NUMBER() OVER (PARTITION BY graph_revisions.workspace_id, graph_edges.edge_id ORDER BY graph_edges.revision DESC) AS reverse_rank
            FROM graph_edges
            INNER JOIN graph_revisions ON graph_revisions.revision = graph_edges.revision
        )
        INSERT INTO graph_edge_identities(workspace_id, edge_id, source_stable_id, target_stable_id, edge_kind, normalized_join_key, first_seen_revision, last_seen_revision)
        SELECT workspace_id, edge_id, source_stable_id, target_stable_id, edge_kind, normalized_join_key, first_seen_revision, last_seen_revision
        FROM ranked_edges
        WHERE reverse_rank = 1;

        INSERT INTO graph_edge_versions(workspace_id, edge_id, valid_from_revision, valid_to_revision, producing_run_id, source_stable_id, target_stable_id, edge_kind, normalized_join_key, provider, confidence, evidence_json)
        SELECT
            graph_revisions.workspace_id,
            graph_edges.edge_id,
            graph_edges.revision,
            CASE WHEN graph_edges.revision = MAX(graph_edges.revision) OVER (PARTITION BY graph_revisions.workspace_id, graph_edges.edge_id) THEN NULL ELSE graph_edges.revision END,
            graph_revisions.run_id,
            graph_edges.source_stable_id,
            graph_edges.target_stable_id,
            graph_edges.edge_kind,
            graph_edges.normalized_join_key,
            graph_edges.provider,
            graph_edges.confidence,
            graph_edges.evidence_json
        FROM graph_edges
        INNER JOIN graph_revisions ON graph_revisions.revision = graph_edges.revision;

        CREATE INDEX ix_graph_node_versions_active ON graph_node_versions(workspace_id, stable_id, valid_from_revision, valid_to_revision);
        CREATE INDEX ix_graph_node_versions_file ON graph_node_versions(workspace_id, file_path, valid_to_revision);
        CREATE INDEX ix_graph_edge_versions_active ON graph_edge_versions(workspace_id, edge_id, valid_from_revision, valid_to_revision);
        CREATE INDEX ix_graph_edge_versions_source ON graph_edge_versions(workspace_id, source_stable_id, valid_to_revision);
        CREATE INDEX ix_graph_edge_versions_target ON graph_edge_versions(workspace_id, target_stable_id, valid_to_revision);
        """;

    private const string MigrationSixSql = """
        CREATE TABLE symbol_identities (
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            symbol_id TEXT NOT NULL,
            node_stable_id TEXT NOT NULL,
            first_seen_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            last_seen_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            PRIMARY KEY(workspace_id, symbol_id),
            FOREIGN KEY(workspace_id, node_stable_id) REFERENCES graph_node_identities(workspace_id, stable_id)
        );

        CREATE TABLE symbol_versions (
            workspace_id TEXT NOT NULL,
            symbol_id TEXT NOT NULL,
            valid_from_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            valid_to_revision INTEGER NULL REFERENCES graph_revisions(revision),
            producing_run_id TEXT NOT NULL REFERENCES analysis_runs(run_id),
            node_stable_id TEXT NOT NULL,
            fully_qualified_name TEXT NOT NULL,
            visibility TEXT NOT NULL,
            normalized_signature TEXT NOT NULL,
            parameter_metadata_json TEXT NOT NULL,
            return_metadata_json TEXT NOT NULL,
            signature_hash TEXT NOT NULL,
            PRIMARY KEY(workspace_id, symbol_id, valid_from_revision),
            FOREIGN KEY(workspace_id, symbol_id) REFERENCES symbol_identities(workspace_id, symbol_id),
            CHECK(valid_to_revision IS NULL OR valid_to_revision >= valid_from_revision)
        );

        CREATE TABLE interface_fingerprint_versions (
            workspace_id TEXT NOT NULL,
            symbol_id TEXT NOT NULL,
            fingerprint_kind TEXT NOT NULL,
            valid_from_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            valid_to_revision INTEGER NULL REFERENCES graph_revisions(revision),
            producing_run_id TEXT NOT NULL REFERENCES analysis_runs(run_id),
            fingerprint_hash TEXT NOT NULL,
            normalized_members_json TEXT NOT NULL,
            PRIMARY KEY(workspace_id, symbol_id, fingerprint_kind, valid_from_revision),
            FOREIGN KEY(workspace_id, symbol_id) REFERENCES symbol_identities(workspace_id, symbol_id),
            CHECK(valid_to_revision IS NULL OR valid_to_revision >= valid_from_revision)
        );

        CREATE INDEX ix_symbol_versions_active ON symbol_versions(workspace_id, symbol_id, valid_from_revision, valid_to_revision);
        CREATE INDEX ix_symbol_versions_signature ON symbol_versions(workspace_id, signature_hash, valid_to_revision);
        CREATE INDEX ix_interface_fingerprint_versions_active ON interface_fingerprint_versions(workspace_id, symbol_id, fingerprint_kind, valid_to_revision);
        """;

    private const string MigrationSevenSql = """
        CREATE TABLE summary_identities (
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            summary_id TEXT NOT NULL,
            target_kind TEXT NOT NULL,
            target_stable_id TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            latest_summary_version_id TEXT NULL,
            PRIMARY KEY(workspace_id, summary_id)
        );

        CREATE TABLE summary_versions (
            summary_version_id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            summary_id TEXT NOT NULL,
            version_number INTEGER NOT NULL,
            source_graph_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            source_repository_commit TEXT NULL,
            summary_text TEXT NOT NULL,
            english_diff TEXT NOT NULL,
            provider TEXT NOT NULL,
            model TEXT NOT NULL,
            provider_metadata_json TEXT NOT NULL,
            staleness TEXT NOT NULL,
            supersedes_summary_version_id TEXT NULL REFERENCES summary_versions(summary_version_id),
            created_at_utc TEXT NOT NULL,
            FOREIGN KEY(workspace_id, summary_id) REFERENCES summary_identities(workspace_id, summary_id),
            UNIQUE(workspace_id, summary_id, version_number)
        );

        CREATE INDEX ix_summary_versions_target ON summary_versions(workspace_id, summary_id, version_number);
        CREATE INDEX ix_summary_versions_source_revision ON summary_versions(workspace_id, source_graph_revision);
        """;

    private const string MigrationThirteenSql = """
        CREATE TABLE analysis_run_repository_provenance (
            run_id TEXT PRIMARY KEY REFERENCES analysis_runs(run_id),
            head_commit TEXT NULL,
            worktree_state TEXT NOT NULL CHECK(worktree_state IN ('clean', 'dirty', 'unavailable')),
            changed_paths_json TEXT NOT NULL,
            captured_at_utc TEXT NOT NULL
        );
        CREATE INDEX ix_analysis_run_repository_provenance_head
            ON analysis_run_repository_provenance(head_commit);
        """;

    private const string MigrationFourteenSql = """
        CREATE TABLE summary_batches (
            summary_batch_id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            session_id TEXT NOT NULL REFERENCES sessions(session_id),
            settle_reason TEXT NOT NULL,
            request_state TEXT NOT NULL CHECK(request_state IN ('pending', 'requested', 'completed', 'degraded', 'skipped')),
            source_graph_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            model_request_metadata_json TEXT NOT NULL,
            created_at_utc TEXT NOT NULL
        );
        CREATE INDEX ix_summary_batches_session
            ON summary_batches(workspace_id, session_id, created_at_utc);
        CREATE INDEX ix_summary_batches_revision
            ON summary_batches(workspace_id, source_graph_revision, created_at_utc);

        CREATE TABLE summary_batch_members (
            summary_batch_id TEXT NOT NULL REFERENCES summary_batches(summary_batch_id),
            member_ordinal INTEGER NOT NULL CHECK(member_ordinal > 0),
            target_kind TEXT NOT NULL,
            target_stable_id TEXT NOT NULL,
            touch_ordinal INTEGER NOT NULL CHECK(touch_ordinal > 0),
            co_touched_member_ordinals_json TEXT NOT NULL,
            PRIMARY KEY(summary_batch_id, member_ordinal),
            UNIQUE(summary_batch_id, target_kind, target_stable_id)
        );
        CREATE INDEX ix_summary_batch_members_target
            ON summary_batch_members(target_kind, target_stable_id, summary_batch_id);

        ALTER TABLE summary_versions
            ADD COLUMN originating_summary_batch_id TEXT NULL REFERENCES summary_batches(summary_batch_id);
        CREATE INDEX ix_summary_versions_originating_batch
            ON summary_versions(originating_summary_batch_id);
        """;

    private const string MigrationFifteenSql = """
        CREATE TABLE model_failure_work_items (
            work_item_id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            work_kind TEXT NOT NULL CHECK(work_kind IN ('summary', 'embedding')),
            summary_batch_id TEXT NULL REFERENCES summary_batches(summary_batch_id),
            target_stable_id TEXT NOT NULL,
            source_graph_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            state TEXT NOT NULL CHECK(state IN ('pending', 'paused', 'disabled', 'completed')),
            failure_kind TEXT NOT NULL,
            attempt_count INTEGER NOT NULL CHECK(attempt_count >= 0),
            next_attempt_at_utc TEXT NULL,
            metadata_json TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL
        );
        CREATE INDEX ix_model_failure_work_items_active
            ON model_failure_work_items(workspace_id, state, next_attempt_at_utc, created_at_utc);
        CREATE INDEX ix_model_failure_work_items_batch
            ON model_failure_work_items(summary_batch_id);

        CREATE TABLE model_failure_work_events (
            work_item_id TEXT NOT NULL REFERENCES model_failure_work_items(work_item_id),
            event_ordinal INTEGER NOT NULL CHECK(event_ordinal > 0),
            state TEXT NOT NULL CHECK(state IN ('pending', 'paused', 'disabled', 'completed')),
            reason_json TEXT NOT NULL,
            occurred_at_utc TEXT NOT NULL,
            PRIMARY KEY(work_item_id, event_ordinal)
        );
        """;

    private const string MigrationEightSql = """
        CREATE TABLE sessions (
            session_id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            client_kind TEXT NOT NULL,
            external_session_id TEXT NULL,
            actor_kind TEXT NOT NULL,
            actor_id TEXT NOT NULL,
            started_at_utc TEXT NOT NULL
        );

        CREATE UNIQUE INDEX ux_sessions_external_identity
            ON sessions(workspace_id, client_kind, external_session_id)
            WHERE external_session_id IS NOT NULL;
        CREATE INDEX ix_sessions_workspace_started
            ON sessions(workspace_id, started_at_utc DESC);

        CREATE TABLE decisions (
            decision_id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            decision_type TEXT NOT NULL,
            resolution TEXT NOT NULL,
            note TEXT NULL,
            actor_kind TEXT NOT NULL,
            actor_id TEXT NOT NULL,
            session_id TEXT NULL REFERENCES sessions(session_id),
            graph_revision INTEGER NULL REFERENCES graph_revisions(revision),
            occurred_at_utc TEXT NOT NULL
        );

        CREATE TABLE decision_targets (
            decision_id TEXT NOT NULL REFERENCES decisions(decision_id),
            target_ordinal INTEGER NOT NULL,
            target_kind TEXT NOT NULL,
            target_stable_id TEXT NOT NULL,
            PRIMARY KEY(decision_id, target_ordinal)
        );

        CREATE INDEX ix_decisions_workspace_occurred
            ON decisions(workspace_id, occurred_at_utc DESC);
        CREATE INDEX ix_decisions_session
            ON decisions(workspace_id, session_id, occurred_at_utc);
        CREATE INDEX ix_decision_targets_lookup
            ON decision_targets(target_kind, target_stable_id, decision_id);

        CREATE TABLE session_events (
            event_id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            session_id TEXT NOT NULL REFERENCES sessions(session_id),
            sequence_number INTEGER NOT NULL CHECK(sequence_number > 0),
            event_type TEXT NOT NULL,
            graph_revision INTEGER NULL REFERENCES graph_revisions(revision),
            target_kind TEXT NULL,
            target_stable_id TEXT NULL,
            decision_id TEXT NULL REFERENCES decisions(decision_id),
            payload_json TEXT NOT NULL,
            occurred_at_utc TEXT NOT NULL,
            UNIQUE(workspace_id, session_id, sequence_number)
        );

        CREATE INDEX ix_session_events_replay
            ON session_events(workspace_id, session_id, sequence_number);
        CREATE INDEX ix_session_events_target
            ON session_events(workspace_id, target_kind, target_stable_id, occurred_at_utc);
        CREATE INDEX ix_session_events_revision
            ON session_events(workspace_id, graph_revision, occurred_at_utc);
        """;

    private const string MigrationNineSql = """
        CREATE TABLE duplicate_finding_identities (
            finding_id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            canonical_pair_key TEXT NOT NULL,
            left_target_kind TEXT NOT NULL,
            left_target_stable_id TEXT NOT NULL,
            right_target_kind TEXT NOT NULL,
            right_target_stable_id TEXT NOT NULL,
            first_seen_graph_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            created_at_utc TEXT NOT NULL,
            UNIQUE(workspace_id, finding_id),
            UNIQUE(workspace_id, canonical_pair_key)
        );

        CREATE TABLE duplicate_finding_observations (
            finding_observation_id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL,
            finding_id TEXT NOT NULL REFERENCES duplicate_finding_identities(finding_id),
            graph_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            aggregation_version TEXT NOT NULL,
            confidence REAL NOT NULL CHECK(confidence >= 0 AND confidence <= 1),
            rationale_json TEXT NOT NULL,
            observed_at_utc TEXT NOT NULL,
            FOREIGN KEY(workspace_id, finding_id) REFERENCES duplicate_finding_identities(workspace_id, finding_id),
            UNIQUE(workspace_id, finding_id, graph_revision, aggregation_version)
        );

        CREATE TABLE duplicate_signal_observations (
            signal_observation_id TEXT PRIMARY KEY,
            finding_observation_id TEXT NOT NULL REFERENCES duplicate_finding_observations(finding_observation_id),
            signal_kind TEXT NOT NULL,
            score REAL NOT NULL CHECK(score >= 0 AND score <= 1),
            evidence_json TEXT NOT NULL,
            observed_at_utc TEXT NOT NULL,
            UNIQUE(finding_observation_id, signal_kind)
        );

        CREATE TABLE duplicate_finding_resolution_links (
            finding_id TEXT NOT NULL REFERENCES duplicate_finding_identities(finding_id),
            decision_id TEXT NOT NULL REFERENCES decisions(decision_id),
            linked_at_utc TEXT NOT NULL,
            PRIMARY KEY(finding_id, decision_id)
        );

        CREATE INDEX ix_duplicate_finding_observations_history
            ON duplicate_finding_observations(workspace_id, finding_id, graph_revision, observed_at_utc);
        CREATE INDEX ix_duplicate_finding_pair
            ON duplicate_finding_identities(workspace_id, left_target_kind, left_target_stable_id, right_target_kind, right_target_stable_id);
        CREATE INDEX ix_duplicate_signal_observations_finding
            ON duplicate_signal_observations(finding_observation_id, signal_kind);
        CREATE INDEX ix_duplicate_finding_resolution_decision
            ON duplicate_finding_resolution_links(decision_id, linked_at_utc);

        CREATE TABLE cluster_revisions (
            cluster_revision_id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            graph_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            algorithm TEXT NOT NULL,
            algorithm_version TEXT NOT NULL,
            input_hash TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            UNIQUE(workspace_id, graph_revision, algorithm, algorithm_version, input_hash)
        );

        CREATE TABLE clusters (
            cluster_id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            cluster_key TEXT NOT NULL,
            created_at_utc TEXT NOT NULL,
            UNIQUE(workspace_id, cluster_key)
        );

        CREATE TABLE cluster_memberships (
            cluster_revision_id TEXT NOT NULL REFERENCES cluster_revisions(cluster_revision_id),
            cluster_id TEXT NOT NULL REFERENCES clusters(cluster_id),
            member_ordinal INTEGER NOT NULL,
            target_kind TEXT NOT NULL,
            target_stable_id TEXT NOT NULL,
            membership_weight REAL NOT NULL CHECK(membership_weight >= 0),
            PRIMARY KEY(cluster_revision_id, cluster_id, member_ordinal),
            UNIQUE(cluster_revision_id, cluster_id, target_kind, target_stable_id)
        );

        CREATE INDEX ix_cluster_revisions_workspace_graph
            ON cluster_revisions(workspace_id, graph_revision, created_at_utc);
        CREATE INDEX ix_cluster_memberships_target
            ON cluster_memberships(target_kind, target_stable_id, cluster_revision_id);

        CREATE TABLE health_snapshots (
            health_snapshot_id TEXT PRIMARY KEY,
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            graph_revision INTEGER NOT NULL REFERENCES graph_revisions(revision),
            calculation_version TEXT NOT NULL,
            score REAL NOT NULL CHECK(score >= 0 AND score <= 100),
            created_at_utc TEXT NOT NULL,
            UNIQUE(workspace_id, graph_revision, calculation_version)
        );

        CREATE TABLE health_metric_components (
            health_snapshot_id TEXT NOT NULL REFERENCES health_snapshots(health_snapshot_id),
            component_key TEXT NOT NULL,
            raw_value REAL NOT NULL,
            weight REAL NOT NULL,
            weighted_contribution REAL NOT NULL,
            detail_json TEXT NOT NULL,
            PRIMARY KEY(health_snapshot_id, component_key)
        );

        CREATE TABLE health_snapshot_resolution_links (
            health_snapshot_id TEXT NOT NULL REFERENCES health_snapshots(health_snapshot_id),
            decision_id TEXT NOT NULL REFERENCES decisions(decision_id),
            PRIMARY KEY(health_snapshot_id, decision_id)
        );

        CREATE INDEX ix_health_snapshots_workspace_graph
            ON health_snapshots(workspace_id, graph_revision, created_at_utc);
        CREATE INDEX ix_health_snapshot_resolution_decision
            ON health_snapshot_resolution_links(decision_id, health_snapshot_id);
        """;

    private const string MigrationTenSql = """
        CREATE INDEX ix_graph_revisions_workspace_revision
            ON graph_revisions(workspace_id, revision DESC);
        CREATE INDEX ix_graph_node_versions_active_by_validity
            ON graph_node_versions(workspace_id, valid_to_revision, stable_id);
        CREATE INDEX ix_graph_node_versions_active_path
            ON graph_node_versions(workspace_id, valid_to_revision, file_path, stable_id);
        CREATE INDEX ix_graph_edge_versions_active_source
            ON graph_edge_versions(workspace_id, valid_to_revision, source_stable_id, edge_kind);
        CREATE INDEX ix_graph_edge_versions_active_target
            ON graph_edge_versions(workspace_id, valid_to_revision, target_stable_id, edge_kind);
        CREATE INDEX ix_graph_edge_versions_active_join
            ON graph_edge_versions(workspace_id, valid_to_revision, edge_kind, normalized_join_key);
        CREATE INDEX ix_session_events_workspace_occurred
            ON session_events(workspace_id, occurred_at_utc, event_id);
        """;

    private const string MigrationElevenSql = """
        CREATE TABLE workspace_graph_states (
            workspace_id TEXT PRIMARY KEY REFERENCES repositories(workspace_id),
            active_graph_revision INTEGER NULL REFERENCES graph_revisions(revision),
            activated_at_utc TEXT NULL
        );

        INSERT INTO workspace_graph_states(workspace_id, active_graph_revision, activated_at_utc)
        SELECT
            repositories.workspace_id,
            (
                SELECT graph_revisions.revision
                FROM graph_revisions
                WHERE graph_revisions.workspace_id = repositories.workspace_id
                ORDER BY graph_revisions.revision DESC
                LIMIT 1
            ),
            (
                SELECT graph_revisions.committed_at_utc
                FROM graph_revisions
                WHERE graph_revisions.workspace_id = repositories.workspace_id
                ORDER BY graph_revisions.revision DESC
                LIMIT 1
            )
        FROM repositories;

        CREATE INDEX ix_workspace_graph_states_active
            ON workspace_graph_states(active_graph_revision);
        """;

    private const string MigrationTwelveSql = """
        CREATE TABLE source_inventory_files (
            workspace_id TEXT NOT NULL REFERENCES repositories(workspace_id),
            repository_relative_path TEXT NOT NULL,
            language TEXT NOT NULL,
            content_hash TEXT NOT NULL,
            byte_length INTEGER NOT NULL CHECK(byte_length >= 0),
            observed_at_utc TEXT NOT NULL,
            PRIMARY KEY(workspace_id, repository_relative_path)
        );

        CREATE INDEX ix_source_inventory_files_workspace_language
            ON source_inventory_files(workspace_id, language, repository_relative_path);
        """;
}
