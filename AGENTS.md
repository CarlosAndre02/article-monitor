# AGENTS.md

## General rules

- Do not commit or push on your own. Make the changes and stop; commit/push only when the user explicitly asks (never as a "wrap up the task" step).
- Only read-only commands against external services and infrastructure (`terraform`, `aws`, `supabase`, `vercel` and equivalents). Inspecting is fine — `terraform plan`, `terraform validate`, `aws ... describe`. Never run one that creates, modifies or destroys: `terraform apply`, `terraform destroy`, `terraform import`, any `aws` call that writes. Hand those over; the user runs them and reports back. The local toolchain is not covered by this.
- Documentation never records current state or anything else that ages ("currently", "so far", "previously"). Write the rule or the reason, not the snapshot.
  - Exception: `docs/adr/` holds Architecture Decision Records and the analyses behind them. They are dated and immutable; a changed decision gets a new ADR that supersedes the old one, whose status is the only thing updated. Rules are in `docs/adr/README.md`.
- Record a decision as an ADR when it changes the structure, is costly to reverse and had a real alternative. Requirements live in `docs/requirements.md`.
- Do not assume anything. Always ask when in doubt.