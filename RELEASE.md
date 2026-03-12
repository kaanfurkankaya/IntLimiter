# Releasing IntLimiter

IntLimiter uses **Velopack** for deployment and auto-updating. The update checks are wired to check GitHub Releases automatically on startup.

## How to Build a Release
1. Run `.\build_release.ps1`.
2. This will:
   - Restore and Build the solution in Release x64 mode.
   - Publish the `IntLimiter.App` as a self-contained executable.
   - Run `vpk pack` to generate the `Setup.exe` and `nupkg` delta files in the `Releases` directory.

## Publishing to GitHub
1. Create a new Release on your GitHub repository.
2. Upload the contents of the `Releases` folder (`Setup.exe`, `RELEASES`, and the `.nupkg` files) to the GitHub release assets.
3. The app will automatically detect this next time it launches and prompt the user.

## WFP / Low-level service Notes
If you compile `IntLimiter.sys` (Kernel C++ driver) in the future, it must be signed with an EV certificate and Microsoft Hardware Portal. You must deploy it either via a custom action during the Velopack install hook, or by having `IntLimiter.Service` install it upon first elevated run via `sc.exe create`.
