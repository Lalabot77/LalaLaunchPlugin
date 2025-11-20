# Syncing work here with your GitHub `main` branch

The container you are chatting with has its own local branch (`work`) and **no remote configured by default**. To keep your GitHub `main` branch aligned with changes produced here, follow these steps.

## Quick start: add your GitHub remote in this workspace
1. Get your repo URL from GitHub (e.g., `https://github.com/<org>/<repo>.git`).
2. In a terminal at the repo root (see “Where to type these commands” below), run:
   ```bash
   git remote add origin <your-repo-url>
   git fetch origin
   git branch -a   # should now show remotes/origin/main
   ```
   If `origin` already exists, you’ll see “remote origin already exists”—that’s fine; continue with the fetch.
3. Check out `main` from the remote:
   ```bash
   git checkout -B main origin/main
   ```
   After this, every change we make here will be on the same `main` branch you use in GitHub Desktop and Visual Studio.

## Where to type these commands
- **GitHub Desktop:** Repository → **Open in Git Bash** (or **Open in Command Prompt**). This opens a terminal already pointed at your cloned repo, so you can paste the commands below.
- **Windows Terminal / PowerShell / Command Prompt:** `cd` into your repo folder (e.g., `C:\Projects\GitHub\LalaLaunchPlugin`) and run the commands there.
- **Visual Studio:** View → **Terminal** or **Developer PowerShell**; it opens at the solution root, ready for Git commands.

Once you have a terminal open in your repository folder, run the steps in order (even if you believe everything is already set
up—`git fetch` is what refreshes what this workspace can see):

1. **Add your GitHub remote (skip if it already exists)**
   ```bash
   git remote -v           # see if "origin" is already configured
   git remote add origin <your-repo-url>
   git fetch origin
   ```
   If `git remote add origin …` prints `remote origin already exists`, that is fine—`origin` is already set up. You still need the `git fetch origin` that follows so the remote branches (like `origin/main`) become visible here; a fetch is harmless to run multiple times.

2. **Check out the GitHub `main` branch locally**
   ```bash
   git checkout -B main origin/main
   ```
   This resets the local `main` branch to match the remote `main`.

   *If you prefer GitHub Desktop:* open the repository, click the branch drop-down in the top bar, choose **main** (or **Choose a branch to checkout…** → search for `main`), and pull the latest changes.

3. **Cherry-pick or merge the local work**
   First, confirm which branches exist **after** `git fetch origin`:
   ```bash
   git branch -a
   ```
   * If `work` is listed under `remotes/origin/` or as a local branch, bring it onto `main`:
   ```bash
   git checkout main
   git cherry-pick work           # for a single-commit branch
   # or
   git merge work                 # if the branch has multiple commits
   ```
   * If `work` does **not** exist (you see “bad revision 'work'”), there is nothing to cherry-pick; stay on `main` and continue.

4. **Build and test locally**
   Run your usual build in Visual Studio or via CLI (e.g., `dotnet build LaunchPlugin.sln`) to verify the branch compiles before pushing.

5. **Push `main` back to GitHub**
   ```bash
   git push origin main
   ```
   After this push, GitHub Desktop will show the updated `main` branch, and you can pull it into your Visual Studio workspace (File → Pull, or the main “Pull origin” button).

6. **Verifying in GitHub Desktop and Visual Studio**
   - In GitHub Desktop, the right panel should show `Current branch: main` and `No local changes` after pulling; if you see a blue banner saying “This branch is behind origin/main,” click **Pull origin**. If you see “Update branch,” it means your feature branch needs to be rebased on `main` before opening a PR.
   - In Visual Studio, confirm the bottom-right status shows `main` and that **Team Explorer → Branches** lists `main` as checked out. Rebuild the solution there to verify the synced code compiles.

6. **Create future PRs from feature branches**
   - Create a feature branch off `main` (e.g., `git checkout -b feature/fix-leader-lap`).
   - Commit and push to GitHub.
   - Open a PR targeting `main`, then merge and pull the result via GitHub Desktop.

Following this sequence ensures the code you build and the code discussed here stay synchronized on the same `main` branch in GitHub, GitHub Desktop, and Visual Studio.
