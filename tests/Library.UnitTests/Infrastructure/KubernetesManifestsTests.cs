namespace Library.UnitTests.Infrastructure;

public class KubernetesManifestsTests
{
    [Fact]
    public void Deployment_liveness_and_readiness_probes_target_distinct_health_endpoints()
    {
        var content = ReadManifest("deployment.yaml");

        Assert.Contains("path: /health/live", content);
        Assert.Contains("path: /health/ready", content);
    }

    [Fact]
    public void Secret_example_only_contains_placeholders_and_no_other_manifest_declares_a_secret()
    {
        var secretExampleContent = ReadManifest("secret.example.yaml");
        Assert.DoesNotContain("kind: Secret", ReadManifest("deployment.yaml"));
        Assert.DoesNotContain("kind: Secret", ReadManifest("service.yaml"));
        Assert.DoesNotContain("kind: Secret", ReadManifest("configmap.yaml"));
        Assert.DoesNotContain("kind: Secret", ReadManifest("migrator-job.yaml"));

        Assert.Contains("kind: Secret", secretExampleContent);
        Assert.Contains("CHANGE_ME", secretExampleContent);
    }

    private static string ReadManifest(string fileName) => File.ReadAllText(Path.Combine(FindK8sDirectory(), fileName));

    private static string FindK8sDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "k8s");
            if (File.Exists(Path.Combine(candidate, "deployment.yaml")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the 'k8s' directory by walking up from the test output directory.");
    }
}
