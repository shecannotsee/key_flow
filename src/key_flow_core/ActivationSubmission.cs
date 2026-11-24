namespace key_flow_core;

public readonly record struct ActivationSubmission(Task Completion, bool Coalesced);
