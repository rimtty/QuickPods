# Default-output policy feasibility project

This historical project isolates the Windows compatibility boundary used to make a verified render endpoint the Console and Multimedia default output.

Microsoft does not publish a general desktop API for setting the default audio endpoint. The spike therefore keeps the policy interface narrow, resolves a unique target, subscribes before mutation, reads state back afterward, and leaves the Communications role unchanged.

It is not the production implementation. Current product code lives in `src/QuickPods.Windows/Audio` and `src/QuickPods.Windows/Bluetooth`.

Only run mutation commands after reviewing the explicit confirmation requirements. Record sanitized results outside the repository and restore the previous default output after testing.
