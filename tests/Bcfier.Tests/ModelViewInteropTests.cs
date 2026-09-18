using System;
using System.IO;
using System.Xml.Serialization;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;
using Xunit;

namespace Bcfier.Tests
{
  public class ModelViewClipMathTests
  {
    [Fact]
    public void CreateSectionBoxFromCrop_ProducesPositiveExtentsAndSixFaces()
    {
      var cropOrigin = new ModelViewClipMath.Vec3(10, 20, 5);
      var cropBasisX = ModelViewClipMath.Vec3.UnitX;
      var cropBasisY = ModelViewClipMath.Vec3.UnitZ;
      var cropBasisZ = ModelViewClipMath.Vec3.UnitY;
      var cropMin = new ModelViewClipMath.Vec3(-4, -3, -1);
      var cropMax = new ModelViewClipMath.Vec3(4, 3, 1);

      ModelViewClipMath.OrientedBox box = ModelViewClipMath.CreateSectionBoxFromCrop(
        cropOrigin, cropBasisX, cropBasisY, cropBasisZ, cropMin, cropMax, farClip: 12);

      Assert.NotNull(box);
      Assert.True(box.Max.X - box.Min.X > 1e-6);
      Assert.True(box.Max.Y - box.Min.Y > 1e-6);
      Assert.True(box.Max.Z - box.Min.Z > 1e-6);

      var faces = ModelViewClipMath.GetOutwardFaces(box);
      Assert.Equal(6, faces.Count);

      // Три пары почти противоположных нормалей
      int oppositePairs = 0;
      for (int i = 0; i < faces.Count; i++)
      {
        for (int j = i + 1; j < faces.Count; j++)
        {
          double dot = ModelViewClipMath.Dot(faces[i].outward, faces[j].outward);
          if (dot < -0.95)
            oppositePairs++;
        }
      }

      Assert.Equal(3, oppositePairs);
    }

    [Fact]
    public void AlignCameraLikeSection_EyeLooksAlongNegatedViewDirection()
    {
      var box = new ModelViewClipMath.OrientedBox
      {
        Origin = ModelViewClipMath.Vec3.Zero,
        BasisX = ModelViewClipMath.Vec3.UnitX,
        BasisY = ModelViewClipMath.Vec3.UnitY,
        BasisZ = ModelViewClipMath.Vec3.UnitZ,
        Min = new ModelViewClipMath.Vec3(-2, -2, -2),
        Max = new ModelViewClipMath.Vec3(2, 2, 2)
      };

      var viewDirection = new ModelViewClipMath.Vec3(0, 1, 0);
      ModelViewClipMath.CameraPose pose = ModelViewClipMath.AlignCameraLikeSection(
        viewDirection,
        ModelViewClipMath.Vec3.UnitZ,
        ModelViewClipMath.Vec3.Zero,
        box,
        viewHeight: 8);

      Assert.NotNull(pose);
      Assert.True(pose.ViewHeight > 0);

      // Forward = −ViewDirection; eye смещён вдоль ViewDirection от центра
      Assert.True(ModelViewClipMath.Dot(pose.Forward, viewDirection) < -0.95);
      Assert.True(pose.Eye.Y > 0);

      double collinear = Math.Abs(ModelViewClipMath.Dot(
        ModelViewClipMath.NormalizeOrFallback(pose.Forward, ModelViewClipMath.Vec3.UnitY),
        ModelViewClipMath.NormalizeOrFallback(pose.Up, ModelViewClipMath.Vec3.UnitZ)));
      Assert.True(collinear < 0.2);
    }

    [Fact]
    public void CreatePlanBoxFromCrop_UsesDepthRange()
    {
      ModelViewClipMath.OrientedBox box = ModelViewClipMath.CreatePlanBoxFromCrop(
        ModelViewClipMath.Vec3.Zero,
        ModelViewClipMath.Vec3.UnitX,
        ModelViewClipMath.Vec3.UnitY,
        ModelViewClipMath.Vec3.UnitZ,
        new ModelViewClipMath.Vec3(-5, -4, 0),
        new ModelViewClipMath.Vec3(5, 4, 0),
        depthMin: -3,
        depthMax: 7);

      Assert.NotNull(box);
      Assert.Equal(-5, box.Min.X, 6);
      Assert.Equal(5, box.Max.X, 6);
      Assert.Equal(-3, box.Min.Z, 6);
      Assert.Equal(7, box.Max.Z, 6);
      Assert.Equal(8, ModelViewClipMath.GetOrientedBoxVertices(box).Length);
    }
  }

  public class ViewpointOpenStrategyTests
  {
    [Fact]
    public void Resolve_SheetFound_OpensSheetEvenIfOrthoPresent()
    {
      Assert.Equal(
        ViewpointOpenAction.OpenSheetView,
        ViewpointOpenStrategy.Resolve(
          hasSheetCamera: true,
          sheetViewFound: true,
          hasOrthogonalCamera: true,
          hasPerspectiveCamera: false));
    }

    [Fact]
    public void Resolve_SheetMissing_WithOrtho_Opens3D()
    {
      Assert.Equal(
        ViewpointOpenAction.OpenOrthogonal3D,
        ViewpointOpenStrategy.Resolve(
          hasSheetCamera: true,
          sheetViewFound: false,
          hasOrthogonalCamera: true,
          hasPerspectiveCamera: false));
    }

    [Fact]
    public void Resolve_SheetMissing_WithoutCamera_Notifies()
    {
      Assert.Equal(
        ViewpointOpenAction.NotifySheetViewMissing,
        ViewpointOpenStrategy.Resolve(
          hasSheetCamera: true,
          sheetViewFound: false,
          hasOrthogonalCamera: false,
          hasPerspectiveCamera: false));
    }

    [Fact]
    public void Resolve_OnlyOrtho_Opens3D()
    {
      Assert.Equal(
        ViewpointOpenAction.OpenOrthogonal3D,
        ViewpointOpenStrategy.Resolve(
          hasSheetCamera: false,
          sheetViewFound: false,
          hasOrthogonalCamera: true,
          hasPerspectiveCamera: false));
    }

    [Fact]
    public void Resolve_Nothing_ReturnsNone()
    {
      Assert.Equal(
        ViewpointOpenAction.None,
        ViewpointOpenStrategy.Resolve(
          hasSheetCamera: false,
          sheetViewFound: false,
          hasOrthogonalCamera: false,
          hasPerspectiveCamera: false));
    }
  }

  public class ModelViewInteropVisinfoTests
  {
    [Fact]
    public void Serialize_Deserialize_PreservesOrthoClippingAndSheetCamera()
    {
      var original = new VisualizationInfo
      {
        OrthogonalCamera = new OrthogonalCamera
        {
          CameraViewPoint = { X = 1.5, Y = 2.5, Z = 3.5 },
          CameraDirection = { X = 0, Y = -1, Z = 0 },
          CameraUpVector = { X = 0, Y = 0, Z = 1 },
          ViewToWorldScale = 4.2
        },
        ClippingPlanes = new[]
        {
          new ClippingPlane
          {
            Location = new Bcfier.Bcf.Bcf2.Point { X = 0, Y = 0, Z = 0 },
            Direction = new Direction { X = 1, Y = 0, Z = 0 }
          },
          new ClippingPlane
          {
            Location = new Bcfier.Bcf.Bcf2.Point { X = 10, Y = 0, Z = 0 },
            Direction = new Direction { X = -1, Y = 0, Z = 0 }
          }
        },
        SheetCamera = new SheetCamera
        {
          SheetID = 42,
          SheetName = "Plan A",
          TopLeft = new Bcfier.Bcf.Bcf2.Point { X = 1, Y = 2, Z = 3 },
          BottomRight = new Bcfier.Bcf.Bcf2.Point { X = 4, Y = 5, Z = 6 }
        }
      };

      var serializer = new XmlSerializer(typeof(VisualizationInfo));
      string xml;
      using (var writer = new StringWriter())
      {
        serializer.Serialize(writer, original);
        xml = writer.ToString();
      }

      Assert.Contains("<OrthogonalCamera>", xml);
      Assert.Contains("<ClippingPlanes>", xml);
      Assert.Contains("<SheetCamera>", xml);
      Assert.Contains("ViewToWorldScale", xml);

      VisualizationInfo loaded;
      using (var reader = new StringReader(xml))
        loaded = (VisualizationInfo)serializer.Deserialize(reader);

      Assert.NotNull(loaded.OrthogonalCamera);
      Assert.Equal(1.5, loaded.OrthogonalCamera.CameraViewPoint.X, 6);
      Assert.Equal(4.2, loaded.OrthogonalCamera.ViewToWorldScale, 6);
      Assert.NotNull(loaded.ClippingPlanes);
      Assert.Equal(2, loaded.ClippingPlanes.Length);
      Assert.NotNull(loaded.SheetCamera);
      Assert.Equal(42, loaded.SheetCamera.SheetID);
      Assert.Equal("Plan A", loaded.SheetCamera.SheetName);
    }

    [Fact]
    public void XmlWithoutSheetCamera_StillLoadsStandardCamera()
    {
      const string xml = @"<?xml version=""1.0"" encoding=""utf-16""?>
<VisualizationInfo xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
  <OrthogonalCamera>
    <CameraViewPoint><X>0</X><Y>0</Y><Z>10</Z></CameraViewPoint>
    <CameraDirection><X>0</X><Y>0</Y><Z>-1</Z></CameraDirection>
    <CameraUpVector><X>0</X><Y>1</Y><Z>0</Z></CameraUpVector>
    <ViewToWorldScale>5</ViewToWorldScale>
  </OrthogonalCamera>
  <ClippingPlanes>
    <ClippingPlane>
      <Location><X>0</X><Y>0</Y><Z>0</Z></Location>
      <Direction><X>0</X><Y>0</Y><Z>1</Z></Direction>
    </ClippingPlane>
  </ClippingPlanes>
</VisualizationInfo>";

      var serializer = new XmlSerializer(typeof(VisualizationInfo));
      VisualizationInfo loaded;
      using (var reader = new StringReader(xml))
        loaded = (VisualizationInfo)serializer.Deserialize(reader);

      Assert.NotNull(loaded.OrthogonalCamera);
      Assert.Equal(5, loaded.OrthogonalCamera.ViewToWorldScale, 6);
      Assert.Null(loaded.SheetCamera);
      Assert.Single(loaded.ClippingPlanes);
    }
  }
}
