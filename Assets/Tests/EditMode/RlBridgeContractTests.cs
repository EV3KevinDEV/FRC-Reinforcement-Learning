using Games.Reefscape.Scoring;
using NUnit.Framework;
using RobotFramework;
using UnityEngine;

namespace MoSimRL.Tests
{
    public class RlBridgeContractTests
    {
        [Test]
        public void ManipulatorIntentUsesHoldForIntakeAndEdgeForPlace()
        {
            Assert.That(new ExternalRobotCommand { ManipulatorIntent = 0.34f }.IntakePressed, Is.True);
            Assert.That(new ExternalRobotCommand { ManipulatorIntent = 0.33f }.IntakePressed, Is.False);
            Assert.That(ExternalRobotCommand.IsPlaceRisingEdge(-0.34f, 0f), Is.True);
            Assert.That(ExternalRobotCommand.IsPlaceRisingEdge(-1f, -0.5f), Is.False);
            Assert.That(ExternalRobotCommand.IsPlaceRisingEdge(-0.33f, 0f), Is.False);
        }

        [Test]
        public void OfficialTableSixTwoValuesAreStable()
        {
            Assert.That(ReefscapeOfficialScoring.Leave, Is.EqualTo(3));
            Assert.That(
                new[]
                {
                    ReefscapeOfficialScoring.AutoL1,
                    ReefscapeOfficialScoring.AutoL2,
                    ReefscapeOfficialScoring.AutoL3,
                    ReefscapeOfficialScoring.AutoL4
                },
                Is.EqualTo(new[] { 3, 4, 6, 7 }));
            Assert.That(
                new[]
                {
                    ReefscapeOfficialScoring.TeleopL1,
                    ReefscapeOfficialScoring.TeleopL2,
                    ReefscapeOfficialScoring.TeleopL3,
                    ReefscapeOfficialScoring.TeleopL4
                },
                Is.EqualTo(new[] { 2, 3, 4, 5 }));
            Assert.That(ReefscapeOfficialScoring.Processor, Is.EqualTo(6));
            Assert.That(ReefscapeOfficialScoring.Net, Is.EqualTo(4));
            Assert.That(ReefscapeOfficialScoring.Park, Is.EqualTo(2));
            Assert.That(ReefscapeOfficialScoring.ShallowCage, Is.EqualTo(6));
            Assert.That(ReefscapeOfficialScoring.DeepCage, Is.EqualTo(12));
        }

        [Test]
        public void ScoreSnapshotIsAnImmutableCompleteCopy()
        {
            var data = new ReefscapeScoreData
            {
                CoralPoints = 7,
                TroughPoints = 3,
                NetPoints = 4,
                ProcessorPoints = 6,
                ClimbPoints = 12,
                ParkPoints = 2,
                LeavePoints = 3,
                CoralScored = 2,
                AlgaeScored = 2
            };
            var snapshot = new ReefscapeScoreSnapshot(data);
            data.CoralPoints = 0;

            Assert.That(snapshot.CoralPoints, Is.EqualTo(7));
            Assert.That(snapshot.TotalPoints, Is.EqualTo(37));
            Assert.That(snapshot.CoralScored, Is.EqualTo(2));
            Assert.That(snapshot.AlgaeScored, Is.EqualTo(2));
        }

        [Test]
        public void ProtocolEnvelopeSerializesVersionAndRequestId()
        {
            var json = JsonUtility.ToJson(new RlResponse
            {
                id = 42,
                ok = true,
                payload = new RlResponsePayload
                {
                    action_dim = 6,
                    observation_dim = 62,
                    team_number = 118
                }
            });

            StringAssert.Contains("\"v\":1", json);
            StringAssert.Contains("\"id\":42", json);
            StringAssert.Contains("\"team_number\":118", json);
            StringAssert.Contains("\"observation_dim\":62", json);
        }
    }
}
