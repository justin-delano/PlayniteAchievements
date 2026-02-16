using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlayniteAchievements.Providers.Epic;
using System;
using System.Net;

namespace PlayniteAchievements.Epic.Tests
{
    [TestClass]
    public class EpicApiClientTests
    {
        [TestMethod]
        public void IsTransientError_ReturnsTrue_ForTimeoutAndNetworkCases()
        {
            Assert.IsTrue(EpicApiClient.IsTransientError(new TimeoutException()));
            Assert.IsTrue(EpicApiClient.IsTransientError(new System.Net.Http.HttpRequestException("network")));
            Assert.IsTrue(EpicApiClient.IsTransientError(new EpicTransientException("transient")));
        }

        [TestMethod]
        public void IsTransientError_ReturnsTrue_For429And5xx()
        {
            Assert.IsTrue(EpicApiClient.IsTransientError(new EpicApiHttpException((HttpStatusCode)429, "429")));
            Assert.IsTrue(EpicApiClient.IsTransientError(new EpicApiHttpException(HttpStatusCode.BadGateway, "502")));
        }

        [TestMethod]
        public void IsTransientError_ReturnsFalse_For4xxNonRetryable()
        {
            Assert.IsFalse(EpicApiClient.IsTransientError(new EpicApiHttpException(HttpStatusCode.BadRequest, "400")));
            Assert.IsFalse(EpicApiClient.IsTransientError(new Exception("other")));
            Assert.IsFalse(EpicApiClient.IsTransientError(null));
        }

        [TestMethod]
        public void EpicAuthResult_IsSuccess_ForAuthenticatedOutcomesOnly()
        {
            var ok = EpicAuthResult.Create(EpicAuthOutcome.Authenticated, "k");
            var already = EpicAuthResult.Create(EpicAuthOutcome.AlreadyAuthenticated, "k");
            var fail = EpicAuthResult.Create(EpicAuthOutcome.Failed, "k");

            Assert.IsTrue(ok.IsSuccess);
            Assert.IsTrue(already.IsSuccess);
            Assert.IsFalse(fail.IsSuccess);
        }
    }
}
