package dev.sporesync.settings;

import dev.sporesync.ssh.SshSettings;

public final class SettingsDependsOnSshProbe {
  private final SshSettings dependency;

  public SettingsDependsOnSshProbe(SshSettings dependency) {
    this.dependency = dependency;
  }
}
