FROM debian:bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends openssh-server python3 \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --create-home --shell /bin/sh scanner \
    && passwd -d scanner \
    && mkdir -p /run/sshd /home/scanner/.ssh /seed/nested /seed/empty /restricted \
    && chmod 700 /restricted \
    && printf 'sample\n' > '/seed/nested/日本語 file.txt' \
    && ssh-keygen -A
COPY authorized_keys /home/scanner/.ssh/authorized_keys
RUN chown -R scanner:scanner /home/scanner/.ssh \
    && chmod 700 /home/scanner/.ssh \
    && chmod 600 /home/scanner/.ssh/authorized_keys
EXPOSE 22
CMD ["/usr/sbin/sshd", "-D", "-e", "-o", "PasswordAuthentication=no", "-o", "PermitRootLogin=no"]
