# Contributing to GroceryPOS

First off, thank you for considering contributing to GroceryPOS! It's people like you that make this tool better for everyone.

## Where do I go from here?

If you've noticed a bug or have a feature request, make one! It's generally best if you get confirmation of your bug or approval for your feature request this way before starting to code.

## Fork & create a branch

If this is something you think you can fix, then fork GroceryPOS and create a branch with a descriptive name.

A good branch name would be (where issue #325 is the ticket you're working on):

```sh
git checkout -b 325-add-new-reporting-feature
```

## Implement your fix or feature

At this point, you're ready to make your changes! Feel free to ask for help; everyone is a beginner at first.

## Make a Pull Request

At this point, you should switch back to your master branch and make sure it's up to date with GroceryPOS's master branch:

```sh
git remote add upstream https://github.com/Muhammad-Talha990/GroceryPOS.git
git checkout master
git pull upstream master
```

Then update your feature branch from your local copy of master, and push it!

```sh
git checkout 325-add-new-reporting-feature
git rebase master
git push --set-upstream origin 325-add-new-reporting-feature
```

Finally, go to GitHub and make a Pull Request.

## Code Style

- Use standard C# naming conventions.
- Keep the MVVM pattern intact (do not put business logic in the XAML Code-Behind).
- Ensure your changes build without warnings in Visual Studio / dotnet CLI.
