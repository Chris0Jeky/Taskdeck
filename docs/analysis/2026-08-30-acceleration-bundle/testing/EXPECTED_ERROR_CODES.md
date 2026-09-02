# Candidate stable error codes

These names are suggestions to unify tests and remediation. Reuse existing Taskdeck error conventions where they already define equivalent codes.

## Context Fabric

- `capture_legacy_payload_invalid`
- `capture_dimension_conflict`
- `capture_producer_not_allowed`
- `processing_job_lease_lost`
- `processing_job_transition_invalid`
- `processing_deadline_exceeded`
- `processing_cost_ceiling_exceeded`
- `processor_session_proof_invalid`
- `processor_protocol_violation`
- `processor_memory_limit_exceeded`
- `representation_parent_invalid`
- `representation_payload_kind_mismatch`
- `evidence_anchor_fields_invalid`
- `blob_declared_size_exceeded`
- `blob_quota_exceeded`
- `blob_reference_owner_mismatch`

## Work model

- `work_parent_self`
- `work_parent_scope_mismatch`
- `work_parent_archived`
- `work_parent_cycle`
- `work_parent_depth_exceeded`
- `work_link_self`
- `work_link_duplicate`
- `work_link_scope_mismatch`
- `work_dependency_cycle`
- `work_assignment_target_ineligible`
- `custom_field_retired`
- `custom_field_type_mismatch`
- `custom_field_option_not_allowed`
- `custom_field_url_scheme_not_allowed`

## Runtime/ops

- `llm_stream_protocol_invalid`
- `llm_streaming_unsupported`
- `llm_structured_output_unsupported`
- `backup_manifest_invalid`
- `backup_checksum_mismatch`
- `connector_key_invalid`
- `connector_credential_corrupt`

Error details should be actionable but content-free where user data/secrets could leak.
