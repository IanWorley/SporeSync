namespace SftpSync.Business.Interface;

public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}
