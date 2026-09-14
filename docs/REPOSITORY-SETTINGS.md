# Repository settings

After the CI workflow has completed successfully on `main`, the repository owner should protect `main` with:

- pull requests required before merging;
- one approving review required;
- stale approvals dismissed when new commits are pushed;
- the `Windows build, test, and portable package` status check required;
- conversations resolved before merging;
- force pushes and branch deletion disabled.

These controls are GitHub repository settings and cannot be enforced by files in the repository alone.
