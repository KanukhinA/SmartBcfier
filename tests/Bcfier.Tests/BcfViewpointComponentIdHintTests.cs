using System;
using System.Collections.ObjectModel;
using Bcfier.Bcf;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data.Utils;
using Xunit;

namespace Bcfier.Tests
{
    public class BcfViewpointComponentIdHintTests
    {
        [Fact]
        public void ApplyAuthoringToolIds_FromComment_AnyCategory()
        {
            Guid exportGuid = Guid.Parse("9db04988-7e4f-4cf5-9103-02548da486b0");
            string ifcGuid = IfcGuid.ToIfcGuid(exportGuid);
            var component = new Component { IfcGuid = ifcGuid };
            var issue = CreateIssueWithComponent(
                component,
                "[Pfosten rechteckig:SP_Schueco_FWS-50_Mull:3094860](9db04988-7e4f-4cf5-9103-02548da486b0) (Member)");

            BcfViewpointComponents.ApplyAuthoringToolIdsFromIssueText(issue);

            Assert.Equal("3094860", component.AuthoringToolId);
        }

        [Fact]
        public void ApplyAuthoringToolIds_FromDescription_DoesNotOverwriteExistingId()
        {
            var component = new Component
            {
                IfcGuid = "abc",
                AuthoringToolId = "111"
            };
            var issue = new Markup
            {
                Topic = new Topic
                {
                    Description = "[Wall:Type:222](9db04988-7e4f-4cf5-9103-02548da486b0)"
                },
                Viewpoints = new ObservableCollection<ViewPoint>
                {
                    new ViewPoint { VisInfo = new VisualizationInfo() }
                }
            };

            BcfViewpointComponents.ApplyAuthoringToolIdsFromIssueText(issue);

            Assert.Equal("111", component.AuthoringToolId);
        }

        [Fact]
        public void ApplyAuthoringToolIds_WithoutGuidMatch_AssignsIdsInCommentOrder()
        {
            var wall = new Component { IfcGuid = "aaaaaaaaaaaaaaaaaaaaaa" };
            var door = new Component { IfcGuid = "bbbbbbbbbbbbbbbbbbbbbb" };
            var issue = new Markup
            {
                Topic = new Topic
                {
                    Description = "[Wall:Type:111](11111111-1111-1111-1111-111111111111) [Door:Type:222](22222222-2222-2222-2222-222222222222)"
                },
                Viewpoints = new System.Collections.ObjectModel.ObservableCollection<ViewPoint>
                {
                    new ViewPoint
                    {
                        VisInfo = new VisualizationInfo
                        {
                            Components = new Components
                            {
                                Visibility = new ComponentVisibility
                                {
                                    Exceptions = new[] { wall, door }
                                }
                            }
                        }
                    }
                }
            };

            BcfViewpointComponents.ApplyAuthoringToolIdsFromIssueText(issue);

            Assert.Equal("111", wall.AuthoringToolId);
            Assert.Equal("222", door.AuthoringToolId);
        }

        [Fact]
        public void ApplyAuthoringToolIds_WithoutMarkdownGuid_StillParsesNumericId()
        {
            var component = new Component { IfcGuid = "cccccccccccccccccccccc" };
            var issue = CreateIssueWithComponent(component, "[Basic Wall:Generic:555] (Member)");

            BcfViewpointComponents.ApplyAuthoringToolIdsFromIssueText(issue);

            Assert.Equal("555", component.AuthoringToolId);
        }

        private static Markup CreateIssueWithComponent(Component component, string commentText)
        {
            return new Markup
            {
                Topic = new Topic { Description = "clash" },
                Comment = new ObservableCollection<Comment>
                {
                    new Comment { Comment1 = commentText }
                },
                Viewpoints = new ObservableCollection<ViewPoint>
                {
                    new ViewPoint
                    {
                        VisInfo = new VisualizationInfo
                        {
                            Components = new Components
                            {
                                Visibility = new ComponentVisibility
                                {
                                    Exceptions = new[] { component }
                                }
                            }
                        }
                    }
                }
            };
        }
    }
}
