﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Credfeto.Date.Interfaces;
using Credfeto.Dotnet.Repo.Git.Exceptions;
using LibGit2Sharp;
using Branch = LibGit2Sharp.Branch;
using CheckoutModifiers = LibGit2Sharp.CheckoutModifiers;
using CheckoutOptions = LibGit2Sharp.CheckoutOptions;
using CloneOptions = LibGit2Sharp.CloneOptions;
using FetchOptions = LibGit2Sharp.FetchOptions;
using PushOptions = LibGit2Sharp.PushOptions;
using Remote = LibGit2Sharp.Remote;
using Repository = LibGit2Sharp.Repository;
using ResetMode = LibGit2Sharp.ResetMode;
using Signature = LibGit2Sharp.Signature;
using TagFetchMode = LibGit2Sharp.TagFetchMode;

namespace Credfeto.Dotnet.Repo.Git;

public static class GitUtils
{
    private const string UPSTREAM = "origin";

    private static readonly FetchOptions FetchOptions = new() { Prune = true, TagFetchMode = TagFetchMode.None };

    private static readonly CheckoutOptions CheckoutOptions = new() { CheckoutModifiers = CheckoutModifiers.Force };

    private static readonly CloneOptions CloneOptions = new()
                                                        {
                                                            Checkout = false,
                                                            IsBare = false,
                                                            RecurseSubmodules = true,
                                                            BranchName = "main",
                                                            FetchOptions = { Prune = true, TagFetchMode = TagFetchMode.All }
                                                        };

    private static readonly PushOptions PushOptions = new();

    private static readonly CommitOptions CommitOptions = new() { AllowEmptyCommit = false, AmendPreviousCommit = false };

    public static string GetFolderForRepo(string repoUrl)
    {
        string work = repoUrl.TrimEnd('/');

        // Extract the folder from the repo name
        string folder = work.Substring(work.LastIndexOf('/') + 1);

        int lastDot = folder.LastIndexOf('.');

        if (lastDot > 0)
        {
            return folder.Substring(startIndex: 0, length: lastDot);
        }

        return folder;
    }

    public static Repository OpenOrClone(string workDir, string repoUrl)
    {
        string repoDir = Path.Combine(path1: workDir, GetFolderForRepo(repoUrl));

        if (Directory.Exists(repoDir))
        {
            Repository repo = OpenRepository(repoDir);

            ResetToMaster(repo);

            return repo;

            // TODO: Also switch to main & fetch
        }

        return CloneRepository(workDir: repoDir, repoUrl: repoUrl);
    }

    public static void ResetToMaster(Repository repo, string upstream = UPSTREAM)
    {
        Remote remote = repo.Network.Remotes[upstream] ?? throw new GitException($"Could not find upstream origin {upstream}");

        string defaultBranch = GetDefaultBranch(repo: repo, upstream: upstream);
        FetchRemote(repo: repo, remote: remote);

        // reset HEAD --hard
        repo.Reset(resetMode: ResetMode.Hard, commit: repo.Head.Tip);

        // & git -C $repoPath clean -f -x -d 2>&1 | Out-Null
        repo.RemoveUntrackedFiles();

        // & git -C $repoPath checkout $defaultBranch 2>&1 | Out-Null
        repo.Checkout(tree: repo.Branches[defaultBranch].Tip.Tree, paths: null, options: CheckoutOptions);

        // & git -C $repoPath reset HEAD --hard 2>&1 | Out-Null
        repo.Reset(resetMode: ResetMode.Hard, commit: repo.Head.Tip);

        // & git -C $repoPath clean -f -x -d 2>&1 | Out-Null
        repo.RemoveUntrackedFiles();

        // & git -C $repoPath fetch --recurse-submodules 2>&1 | Out-Null

        // # NOTE Loses all local commits on master
        // & git -C $repoPath reset --hard $upstreamBranch 2>&1 | Out-Null
        repo.Reset(resetMode: ResetMode.Hard, commit: repo.Branches[upstream + "/" + defaultBranch].Tip);

        // & git -C $repoPath remote update $upstream --prune 2>&1 | Out-Null
        FetchRemote(repo: repo, remote: remote);

        // & git -C $repoPath prune 2>&1 | Out-Null

        if (HasUncommittedChanges(repo: repo))
        {
            throw new GitException("Failed to reset to " + defaultBranch + " - uncommitted changes");
        }
    }

    private static void FetchRemote(Repository repo, Remote remote)
    {
        repo.Network.Fetch(url: remote.Name, remote.FetchRefSpecs.Select(r => r.Specification), options: FetchOptions);
    }

    public static void RemoveAllLocalBranches(Repository repo)
    {
        IReadOnlyList<Branch> branchesToRemove = repo.Branches.Where(IsLocalBranch)
                                                     .ToArray();

        foreach (Branch branch in branchesToRemove)
        {
            if (StringComparer.Ordinal.Equals(x: repo.Head.CanonicalName, y: branch.CanonicalName))
            {
                // don't try and delete the current branch
                continue;
            }

            repo.Branches.Remove(branch);
        }

        static bool IsLocalBranch(Branch branch)
        {
            return !branch.IsRemote;
        }
    }

    public static IReadOnlyCollection<string> GetRemoteBranches(Repository repo, string upstream = UPSTREAM)
    {
        const string prefix = "refs/heads/";

        return repo.Branches.Where(IsRemoteBranch)
                   .Select(b => b.UpstreamBranchCanonicalName.Substring(prefix.Length))
                   .Where(b => !StringComparer.Ordinal.Equals(x: b, y: "HEAD"))
                   .ToArray();

        bool IsRemoteBranch(Branch branch)
        {
            return branch.IsRemote && StringComparer.Ordinal.Equals(x: branch.RemoteName, y: upstream) &&
                   branch.UpstreamBranchCanonicalName.StartsWith(value: prefix, comparisonType: StringComparison.Ordinal);
        }
    }

    public static string GetDefaultBranch(Repository repo, string upstream = UPSTREAM)
    {
        Branch headBranch = repo.Branches.FirstOrDefault(IsHeadBranch) ?? throw new GitException($"Failed to find remote branches for {upstream}");
        string target = headBranch.Reference.TargetIdentifier ?? throw new GitException($"Failed to find remote branches for {upstream}");
        string prefix = string.Concat(str0: "refs/remotes/", str1: upstream, str2: "/");

        if (target.StartsWith(value: prefix, comparisonType: StringComparison.Ordinal))
        {
            return target.Substring(prefix.Length);
        }

        throw new GitException($"Failed to find HEAD branch for remote {upstream}");

        bool IsHeadBranch(Branch branch)
        {
            return branch.IsRemote && StringComparer.Ordinal.Equals(x: branch.RemoteName, y: upstream) && StringComparer.Ordinal.Equals(x: branch.UpstreamBranchCanonicalName, y: "refs/heads/HEAD");
        }
    }

    private static Repository CloneRepository(string workDir, string repoUrl)
    {
        string? path = Repository.Clone(sourceUrl: repoUrl, workdirPath: workDir, options: CloneOptions);

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new GitException($"Failed to clone repo {repoUrl} to {workDir}");
        }

        return OpenRepository(path);
    }

    public static bool HasUncommittedChanges(Repository repo)
    {
        return repo.RetrieveStatus()
                   .IsDirty;

/*
     function Git-HasUnCommittedChanges {
   param(
       [string] $repoPath = $(throw "Git-HasUnCommittedChanges: repoPath not specified")
       )

       Log -message "Git-HasUnCommittedChanges: $repoPath"

       [string]$repoPath = GetRepoPath -repoPath $repoPath

       [string[]]$result = git -C $repoPath diff --no-patch --exit-code 2>&1
       if(!$?) {
           Log-Batch -messages $result
           return $true
       }

       Log-Batch -messages $result
       return $false
   }
 */
    }

    public static void Commit(Repository repo, string message, ICurrentTimeSource currentTimeSource)
    {
        repo.Index.Add("*");
        repo.Index.Write();

        Signature author = new(name: "Example", email: "example@example.com", when: currentTimeSource.UtcNow());
        Signature committer = author;
        repo.Commit(message: message, author: author, committer: committer, options: CommitOptions);

        /* function Git-Commit {
             param(
                 [string] $repoPath = $(throw "Git-Commit: repoPath not specified"),
                 [string] $message = $(throw "Git-Commit: message not specified")
                 )

                 Log -message "Git-Commit: $repoPath ($message)"

                 [string]$repoPath = GetRepoPath -repoPath $repoPath

                 & git -C $repoPath add -A 2>&1 | Out-Null
                 & git -C $repoPath commit -m"$message" 2>&1 | Out-Null
             }
            */
    }

    public static void CommitNamed(Repository repo, string message, params string[] files)
    {
        /*
            function Git-Commit-Named {
            param(
                [string] $repoPath = $(throw "Git-Commit-Named: repoPath not specified"),
                [string] $message = $(throw "Git-Commit-Named: message not specified"),
                [String[]] $files = $(throw "Git-Commit-Named: files not specified")
                )

                Log -message "Git-Commit-Named: $repoPath ($message)"

                [string]$repoPath = GetRepoPath -repoPath $repoPath

                foreach($file in $files) {
                    [string]$fileUnix = $file.Replace("\", "/")
                    Log -message "Staging $fileUnix"
                    & git -C $repoPath add $fileUnix 2>&1 | Out-Null
                }

                & git -C $repoPath commit -m"$message" 2>&1 | Out-Null
            }
            */
    }

    public static void Push(Repository repo, string upstream = UPSTREAM)
    {
        Remote remote = repo.Network.Remotes[upstream] ?? throw new GitException($"Could not find upstream origin {upstream}");

        repo.Network.Push(remote: remote, remote.PushRefSpecs.Select(r => r.Specification), pushOptions: PushOptions);

        /*
            function Git-Push {
           param(
               [string] $repoPath = $(throw "Git-Push: repoPath not specified")
               )

               Log -message "Git-Push: $repoPath"

               [string]$repoPath = GetRepoPath -repoPath $repoPath

               & git -C $repoPath push | Out-Null
           }
        */
    }

    public static void PushOrigin(Repository repo, string branchName, string upstream = UPSTREAM)
    {
        Remote remote = repo.Network.Remotes[upstream] ?? throw new GitException($"Could not find upstream origin {upstream}");

        repo.Network.Push(remote: remote, remote.PushRefSpecs.Select(r => r.Specification), pushOptions: PushOptions);

        /*

           function Git-PushOrigin {
           param(
               [string] $repoPath = $(throw "Git-PushOrigin: repoPath not specified"),
               [string] $branchName = $(throw "Git-PushOrigin: branchName not specified")
               )

               Log -message "Git-PushOrigin: $repoPath ($branchName)"

               [string]$upstream = "origin";

               Git-ValidateBranchName -branchName $branchName -method "Git-PushOrigin"

               [string]$repoPath = GetRepoPath -repoPath $repoPath

               & git -C $repoPath push --set-upstream $upstream $branchName -v 2>&1 | Out-Null
           }

         */
    }

    public static bool DoesBranchExist(Repository repo, string branchName, string upstream = UPSTREAM)
    {
        return repo.Branches.Any(b => StringComparer.Ordinal.Equals(x: b.FriendlyName, y: branchName));

        /*
         function Git-DoesBranchExist {
           param(
               [string] $repoPath = $(throw "Git-DoesBranchExist: repoPath not specified"),
               [string] $branchName = $(throw "Git-DoesBranchExist: branchName not specified")
               )

               Log -message "Git-DoesBranchExist: $repoPath ($branchName)"

               [string]$upstream = "origin";
               [string]$repoPath = GetRepoPath -repoPath $repoPath

               [string]$defaultBranch = Git-GetDefaultBranch -repoPath $repoPath -upstream $upstream
               [string]$upstreamBranch = "$upstream/$defaultBranch"

               Git-ValidateBranchName -branchName $branchName -method "Git-DoesBranchExist"

               [string[]]$result = git -C $repoPath branch --remote 2>&1

               [string]$regex = $branchName.replace(".", "\.") + "$"

               $result -match $regex
               if($result -eq $null) {
            return $false;
               }

               [string]$result = $result.Trim()
               if($result -eq $branchName) {
                   return $true
               }

               [string]$upstreamBranch = "$upstream/$branchName"
               if($result -eq $upstreamBranch) {
                   return $true
               }

               return $false
           }
         */
    }

    public static void CreateBranch(Repository repo, string branchName, string upstream = UPSTREAM)
    {
        /*
         function Git-DeleteBranch {
           param(
               [string] $repoPath = $(throw "Git-DeleteBranch: repoPath not specified"),
               [string] $branchName = $(throw "Git-DeleteBranch: branchName not specified")
               )

               Log -message "Git-DeleteBranch: $repoPath ($branchName)"

               [string]$upstream = "origin"

               Git-ValidateBranchName -branchName $branchName -method "Git-DeleteBranch"

               [string]$repoPath = GetRepoPath -repoPath $repoPath

               [bool]$branchExists = Git-DoesBranchExist -branchName $branchName -repoPath $repoPath
               if($branchExists) {
                   & git -C $repoPath branch -D $branchName 2>&1 | Out-Null
               }

               [string]$upstreamBranch = "$upstream/$branchName"
               [bool]$branchExists = Git-DoesBranchExist -branchName $upstreamBranch -repoPath $repoPath
               if($branchExists) {
                   & git -C $repoPath push $upstream ":$branchName" 2>&1 | Out-Null
               }

               return $true;
           }
         */
    }

    public static void ValidateBranchName(string branchName)
    {
        /*
          function Git-ValidateBranchName {
             param (
                 [string] $branchName = $(throw "Git-ValidateBranchName: branchName not specified"),
                 [string] $method = $(throw "Git-ValidateBranchName: method not specified")

             )

                 if($branchName -eq $null) {
                     throw "$($method) : Invalid branch (null)"
                 }

                 if($branchName -eq "") {
                     throw "$($method) : Invalid branch: [$branchName]"
                 }

                 if($branchName.Contains("//")) {
                     throw "$($method) : Invalid branch: [$branchName]"
                 }
             }
         */
    }

    public static void Renormalize(Repository repo)
    {
        /*
         function Git-ReNormalise {
           param(
               [string] $repoPath = $(throw "Git-ReNormalise: repoPath not specified")
               )

               Log -message "Git-ReNormalise: $repoPath"

               [string]$repoPath = GetRepoPath -repoPath $repoPath

               & git -C $repoPath add . --renormalize 2>&1 | Out-Null
               [bool]$hasChanged = Git-HasUnCommittedChanges -repoPath $repoPath
               if($hasChanged -eq $true) {
                   & git -C $repoPath commit -m"Renormalised files" 2>&1 | Out-Null
                   & git -C $repoPath push 2>&1 | Out-Null
               }
           }
         */
    }

    public static string GetHeadRev(Repository repository)
    {
        /*
         function Git-Get-HeadRev {
           param(
               [string] $repoPath = $(throw "Git-Get-HeadRev: repoPath not specified")
               )

               Log -message "Git-Get-HeadRev: $repoPath"

               [string]$repoPath = GetRepoPath -repoPath $repoPath

               [string[]]$result = git -C $repoPath rev-parse HEAD 2>&1

               Log -message "Head Rev"
               Log-Batch -messages $result

               if(!$?) {
                   Log-Batch -messages $result
                   Log -message "Failed to get head rev"
                   return $null
               }

               [string]$rev = $result.Trim()
               Log -message "Head Rev: $rev"

               return $rev
           }
         */

        return repository.Head.Tip.Sha;
    }

    public static bool HasSubmodules(Repository repo)
    {
        return repo.Submodules.Any();
        /*
         function Git-HasSubmodules {
               param(
               [string] $repoPath = $(throw "Git-HasSubmodules: repoPath not specified")
               )

               Log -message "Git-HasSubmodules: $repoPath"

               [string]$repoPath = GetRepoPath -repoPath $repoPath

               [string[]]$result = git -C $repoPath submodule 2>&1

               if(!$?) {
                   Log-Batch -messages $result
                   Log -message "Failed to get submodules."
                   return $false
               }

               if($result -eq $null -or $result.Trim() -eq "")  {
                   return $false
               }

               Log -message "Submodules found:"
               Log -message $result

               return $true
           }
         */
    }

    public static void RemoveBranchesForPrefix(Repository repo, string branchForUpdate, string branchPrefix)
    {
        /*
          function Git-RemoveBranchesForPrefix {
             param(
                 [string]$repoPath = $(throw "Git-RemoveBranchesForPrefix: repoPath not specified"),
                 [string]$branchForUpdate = $(throw "Git-RemoveBranchesForPrefix: branchForUpdate not specified"),
                 [string]$branchPrefix = $(throw "Git-RemoveBranchesForPrefix: branchPrefix not specified")
                 )

                 Log -message "Git-RemoveBranchesForPrefix: $repoPath ($branchForUpdate, $branchPrefix)"

                 [string]$upstream = "origin"

                 Git-ValidateBranchName -branchName $branchPrefix -method "Git-RemoveBranchesForPrefix"

                 [string[]]$remoteBranches = Git-GetRemoteBranches -repoPath $repoFolder -upstream $upstream

                 Log -message "Looking for branches to remove based on prefix: $branchPrefix"
                 foreach($branch in $remoteBranches) {
                     if($branchForUpdate) {
                         if($branch -eq $branchForUpdate) {
                             Log -message "- Skipping branch just pushed to: $branch"
                             continue
                         }
                     }

                     if($branch.StartsWith($branchPrefix)) {
                         Log -message "+ Deleting older branch for package: $branch"
                         Git-DeleteBranch -branchName $branch -repoPath $repoFolder
                     }
                 }
             }
         */
    }

    public static DateTimeOffset GetLastCommitDate(Repository repo)
    {
        return repo.Head.Tip.Author.When;

        /*
          function Get-GetLastCommitDate {
             param(
                 [string] $repoPath = $(throw "Get-GetLastCommitDate: repoPath not specified")
                 )

                 Log -message "Git-GetLastCommitDate: $repoPath"

                 [string]$repoPath = GetRepoPath -repoPath $repoPath

                 $unixTime = git -C $repoPath log -1 --format=%ct 2>&1

                 [DateTime]$when = [DateTimeOffset]::FromUnixTimeSeconds($unixTime).UtcDateTime

                 return $wh
         */
    }

    private static Repository OpenRepository(string workDir)
    {
        string found = Repository.Discover(workDir);

        return new(found);
    }
}