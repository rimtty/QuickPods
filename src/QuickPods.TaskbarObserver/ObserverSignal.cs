using QuickPods.Contracts;

namespace QuickPods.TaskbarObserver;

internal readonly record struct ObserverSignal(
    ObserverInvalidationKind Kind,
    ObserverSourceClassification Source);
