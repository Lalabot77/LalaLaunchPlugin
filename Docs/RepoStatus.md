# Repository status and how to sync with GitHub

## What exists in this checkout right now
- Only one local branch is present: `work`.
- There is no Git remote configured, so nothing in this checkout is currently linked to GitHub.
- The latest commit on `work` is `1b8efb1` with the message “Ensure live snapshot car/track labels refresh.” Use `git log --oneline -n 5` to see the last few commits if you want to double-check.

## How to connect this checkout to your GitHub repo
1. Add your GitHub remote (replace the URL with your actual repository clone URL):
   ```bash
   git remote add origin https://github.com/<your-account>/LalaLaunchPlugin.git
   ```
2. Fetch all remote branches so you can see what already exists on GitHub:
   ```bash
   git fetch --all
   ```
3. List branches to confirm you can now see the remote ones:
   ```bash
   git branch -a
   ```

## How to push the current work to GitHub
- If you want the `work` branch to become your GitHub `main` (replacing or updating it):
  ```bash
  git push -u origin work:main
  ```
- If you prefer to keep `work` separate and open a pull request later, push it as its own branch:
  ```bash
  git push -u origin work
  ```

## If GitHub already has different commits on `main`
- First, fetch as above, then inspect remote history (example):
  ```bash
  git log --oneline origin/main | head
  ```
- To keep your current work safe, create a backup branch locally before attempting any merges or rebases:
  ```bash
  git branch backup/work-local
  ```
- To combine remote `main` with your local changes, check out `main` tracking the remote and merge/rebase `work` onto it:
  ```bash
  git checkout -b main origin/main
  git merge work
  ```
  Resolve any conflicts that appear, commit the resolution, then push:
  ```bash
  git push -u origin main
  ```

## Where to ask SimHub for car/track live data
- SimHub exposes the live car and track via data references such as `DataCorePlugin.GameData.CarModel` and `DataCorePlugin.GameData.TrackNameWithConfig`. The UI dropdowns use these live values, so once your GitHub branches are synced you can verify that both the pre-race and snapshot fields bind to the same live strings.
