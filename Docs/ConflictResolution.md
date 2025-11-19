# Resolving the GitHub merge conflicts

The open pull request was built from the **`work` branch** and GitHub is showing conflicts with `main`. In GitHub's conflict editor the columns are labeled **Current change** (what is already on `main`) and **Incoming change** (what the PR branch proposes). Because the recent snapshot/UI work lives on the PR branch, you generally want to keep the **incoming** side when choosing between the two.

## One-shot: keep the PR branch changes
1. Click **Resolve conflicts** on the PR.
2. For each file:
   * If the block represents the new live-session snapshot work, pick **Accept incoming change** to retain the PR edits.
   * Only choose **Accept current change** if `main` has a newer fix you must keep.
3. After all conflicts are resolved, click **Mark as resolved** and **Commit merge**.

This mirrors the command-line equivalent:

```bash
# from main with the PR branch fetched as `work`
git checkout main
git merge work --strategy-option theirs
```

## Safer option: resolve locally with a three-way merge
1. Fetch and check out both branches:
   ```bash
   git fetch origin
   git checkout main
   git pull
   git checkout -b merge-work
   git merge origin/work
   ```
2. For each conflict, keep the edits from `work` unless `main` has a deliberate fix you need. Tools like **VS Code's Merge Editor** or **`git mergetool`** help avoid mistakes.
3. Build locally to ensure XAML parses cleanly:
   ```bash
   dotnet build LaunchPlugin.sln
   ```
4. Push the resolved branch and update the PR:
   ```bash
   git push -u origin merge-work
   ```

## How to choose per-file
* **FuelCalcs.cs** – keep the incoming (PR) changes to preserve the live snapshot metrics and SimHub car/track bindings.
* **FuelCalculatorView.xaml / LaunchSummaryExpander.xaml** – keep incoming changes; they contain the new snapshot expander and bindings.
* **LalaLaunch.cs** – keep incoming changes; they feed the live snapshot car/track and pit-loss data.

If you see a conflict where `main` has a hotfix that is not in the PR, copy that specific fix into the merged file before committing.

## If GitHub still blocks the merge
* Ensure every conflict shows **Resolved** in the PR UI.
* Re-run the PR's checks (if any) after committing the merge.
* If an automatic build fails due to XAML, open the file locally and ensure the markup still compiles, then push a fix to the PR branch.
