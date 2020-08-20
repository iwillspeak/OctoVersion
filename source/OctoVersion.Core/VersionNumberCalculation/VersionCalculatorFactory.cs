﻿using System.Collections.Generic;
using System.Linq;
using LibGit2Sharp;
using Serilog;

namespace OctoVersion.Core.VersionNumberCalculation
{
    public class VersionCalculatorFactory
    {
        private readonly ILogger _logger = Log.ForContext<VersionCalculatorFactory>();
        private readonly Repository _repository;

        public VersionCalculatorFactory(string repositorySearchPath)
        {
            var gitRepositoryPath = Repository.Discover(repositorySearchPath);
            _logger.Debug("Located Git repository in {GitRepositoryPath}", gitRepositoryPath);
            _repository = new Repository(gitRepositoryPath);
        }

        public VersionCalculator Create()
        {
            Commit[] allCommits;
            using (_logger.BeginTimedOperation("Loading commits"))
            {
                allCommits = _repository.Commits.ToArray();
                _logger.Debug("Repository contains {NumberOfCommits} commits", allCommits.Length);
            }

            Dictionary<string, SimpleCommit> commits;
            using (_logger.BeginTimedOperation("Mapping commits into internal representation"))
            {
                commits = allCommits
                    .Select(SimpleCommit.FromCommit)
                    .ToDictionary(c => c.Hash, c => c);
            }

            // Establish parent/child relationships
            using (_logger.BeginTimedOperation("Establishing parent/child relationships"))
            {
                foreach (var commit in allCommits)
                {
                    if (!commit.Parents.Any()) continue;

                    var simpleCommit = commits[commit.Sha];
                    foreach (var parent in commit.Parents)
                    {
                        var simpleParent = commits[parent.Sha];
                        simpleCommit.AddParent(simpleParent);
                    }
                }
            }

            Tag[] allTags;
            using (_logger.BeginTimedOperation("Loading tags"))
            {
                allTags = _repository.Tags.ToArray();
                _logger.Debug("Repository contains {NumberOfTags} tags", allTags.Length);
            }

            using (_logger.BeginTimedOperation("Applying relevant version tags to each commit"))
            {
                foreach (var tag in allTags)
                {
                    var version = VersionInfo.TryParse(tag.FriendlyName);
                    if (version == null) continue;

                    // tags can reference to commits which have been removed. In this case we don't care.
                    if (!commits.TryGetValue(tag.Target.Sha, out var commit)) continue;

                    _logger.Verbose("{CommitHash} is tagged with {VersionTag}", commit.Hash, version);
                    commit.TagWith(version);
                }
            }

            var currentCommitHash = allCommits.First().Sha;
            var calculator = new VersionCalculator(commits.Values.ToArray(), currentCommitHash);
            return calculator;
        }
    }
}