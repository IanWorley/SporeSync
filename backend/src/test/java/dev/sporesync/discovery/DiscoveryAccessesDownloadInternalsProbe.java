package dev.sporesync.discovery;

import dev.sporesync.downloads.internal.DownloadJobRepository;

public final class DiscoveryAccessesDownloadInternalsProbe {
  private final DownloadJobRepository dependency;

  public DiscoveryAccessesDownloadInternalsProbe(DownloadJobRepository dependency) {
    this.dependency = dependency;
  }
}
