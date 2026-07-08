using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Options;
using Demo_3dDatasCheck_VueTsAspNetCore10.Server.Services;
using Microsoft.Extensions.Options;

namespace Demo_3dDatasCheck_VueTsAspNetCore10.Server.Tests.Services;

public class BuildingProcessorServiceTests
{
    private static readonly BuildingAbnormalDetectionOptions DefaultDetection = new();

    [Fact]
    public void ProcessXml_LegacyXmlWithJsonBoundedBy_ParsesExpectedFields()
    {
        var service = CreateService();
        var xml = """
            <ArrayOfConsistsOfBuildingPart>
              <ConsistsOfBuildingPart>
                <MID>1001</MID>
                <OID>2002</OID>
                <建號母號>A001</建號母號>
                <層次>001</層次>
                <boundedBy>[[[0,0,0],[4,0,0],[4,4,3.2],[0,4,3.2],[0,0,0]]]</boundedBy>
              </ConsistsOfBuildingPart>
            </ArrayOfConsistsOfBuildingPart>
            """;

        var result = service.ProcessXml(xml);

        var building = Assert.Single(result);
        Assert.Equal("1001", building.Mid);
        Assert.Equal("2002", building.Oid);
        Assert.Equal("A001", building.BuildingNo);
        Assert.Equal("001", building.Floor);
        Assert.Single(building.Coordinates);
        Assert.Equal(5, building.Coordinates[0].Count);
        Assert.Equal(0, building.MinHeight);
        Assert.Equal(3.2, building.MaxHeight);
    }

    [Fact]
    public void ProcessXml_CityGmlPropertyBuilding_UsesGmlIdAndParsesPolygons()
    {
        var service = CreateService();
        var xml = """
            <CityModel xmlns="http://www.opengis.net/citygml/2.0"
                       xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
                       xmlns:gml="http://www.opengis.net/gml"
                       xmlns:pb="https://land.moi.gov.tw/schema/propertybuilding">
              <cityObjectMember>
                <pb:產權建物>
                  <bldg:consistsOfBuildingPart>
                    <pb:建物產權空間 gml:id="id001">
                      <bldg:boundedBy>
                        <bldg:InteriorWallSurface>
                          <bldg:lod4MultiSurface>
                            <gml:MultiSurface>
                              <gml:surfaceMembers>
                                <gml:Polygon>
                                  <gml:exterior>
                                    <gml:LinearRing>
                                      <gml:posList>251582.6776065464 2742712.2278096904 0.00 251587.1797726064 2742721.7136149006 0.00 251582.66272250641 2742723.8575035008 0.00 251578.1605564464 2742714.3716982906 0.00 251582.6776065464 2742712.2278096904 0.00</gml:posList>
                                    </gml:LinearRing>
                                  </gml:exterior>
                                </gml:Polygon>
                                <gml:Polygon>
                                  <gml:exterior>
                                    <gml:LinearRing>
                                      <gml:posList>251582.6776065464 2742712.2278096904 4.20 251587.1797726064 2742721.7136149006 4.20 251582.66272250641 2742723.8575035008 4.20 251578.1605564464 2742714.3716982906 4.20 251582.6776065464 2742712.2278096904 4.20</gml:posList>
                                    </gml:LinearRing>
                                  </gml:exterior>
                                </gml:Polygon>
                                <gml:Polygon>
                                  <gml:exterior>
                                    <gml:LinearRing>
                                      <gml:posList>251582.6776065464 2742712.2278096904 0.00 251587.1797726064 2742721.7136149006 0.00 251587.1797726064 2742721.7136149006 3.20 251582.6776065464 2742712.2278096904 3.20 251582.6776065464 2742712.2278096904 0.00</gml:posList>
                                    </gml:LinearRing>
                                  </gml:exterior>
                                </gml:Polygon>
                                <gml:Polygon>
                                  <gml:exterior>
                                    <gml:LinearRing>
                                      <gml:posList>251587.1797726064 2742721.7136149006 0.00 251582.66272250641 2742723.8575035008 0.00 251582.66272250641 2742723.8575035008 3.20 251587.1797726064 2742721.7136149006 3.20 251587.1797726064 2742721.7136149006 0.00</gml:posList>
                                    </gml:LinearRing>
                                  </gml:exterior>
                                </gml:Polygon>
                                <gml:Polygon>
                                  <gml:exterior>
                                    <gml:LinearRing>
                                      <gml:posList>251582.66272250641 2742723.8575035008 0.00 251578.1605564464 2742714.3716982906 0.00 251578.1605564464 2742714.3716982906 3.20 251582.66272250641 2742723.8575035008 3.20 251582.66272250641 2742723.8575035008 0.00</gml:posList>
                                    </gml:LinearRing>
                                  </gml:exterior>
                                </gml:Polygon>
                                <gml:Polygon>
                                  <gml:exterior>
                                    <gml:LinearRing>
                                      <gml:posList>251578.1605564464 2742714.3716982906 0.00 251582.6776065464 2742712.2278096904 0.00 251582.6776065464 2742712.2278096904 3.20 251578.1605564464 2742714.3716982906 3.20 251578.1605564464 2742714.3716982906 0.00</gml:posList>
                                    </gml:LinearRing>
                                  </gml:exterior>
                                </gml:Polygon>
                              </gml:surfaceMembers>
                            </gml:MultiSurface>
                          </bldg:lod4MultiSurface>
                        </bldg:InteriorWallSurface>
                      </bldg:boundedBy>
                      <pb:建號母號>02282</pb:建號母號>
                      <pb:建號子號>000</pb:建號子號>
                      <pb:是否為主要建物>true</pb:是否為主要建物>
                      <pb:產權高度>0.000</pb:產權高度>
                      <pb:面積>52.500</pb:面積>
                      <pb:層次>001</pb:層次>
                    </pb:建物產權空間>
                  </bldg:consistsOfBuildingPart>
                </pb:產權建物>
              </cityObjectMember>
            </CityModel>
            """;

        var result = service.ProcessXml(xml);

        var building = Assert.Single(result);
        Assert.Equal("id001", building.Mid);
        Assert.Equal("id001", building.Oid);
        Assert.Equal("02282", building.BuildingNo);
        Assert.Equal("001", building.Floor);
        Assert.Equal(6, building.Coordinates.Count);
        Assert.All(building.Coordinates, polygon => Assert.True(polygon.Count >= 5));
        Assert.Equal(0, building.MinHeight);
        Assert.Equal(4.2, building.MaxHeight);
        Assert.All(building.Coordinates.SelectMany(polygon => polygon), point =>
        {
            Assert.InRange(point[0], 119d, 123d);
            Assert.InRange(point[1], 21d, 26d);
        });
        Assert.Contains(building.FixMessages, message => message.Contains("座標系統推定"));
        Assert.DoesNotContain("座標 JSON 格式解析失敗", building.ErrorMessages);
    }

    [Fact]
    public void ProcessXml_CityGml2DPosList_FillsMissingZWithZero()
    {
        var service = CreateService();
        var xml = """
            <CityModel xmlns="http://www.opengis.net/citygml/2.0"
                       xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
                       xmlns:gml="http://www.opengis.net/gml"
                       xmlns:pb="https://land.moi.gov.tw/schema/propertybuilding">
              <cityObjectMember>
                <pb:建物產權空間 gml:id="id2d">
                  <bldg:boundedBy>
                    <bldg:WallSurface>
                      <bldg:lod2MultiSurface>
                        <gml:MultiSurface>
                          <gml:surfaceMember>
                            <gml:Polygon>
                              <gml:exterior>
                                <gml:LinearRing>
                                  <gml:posList srsDimension="2">0 0 2 0 2 2 0 2 0 0</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Polygon>
                          </gml:surfaceMember>
                        </gml:MultiSurface>
                      </bldg:lod2MultiSurface>
                    </bldg:WallSurface>
                  </bldg:boundedBy>
                  <pb:建號母號>B002</pb:建號母號>
                  <pb:層次>002</pb:層次>
                </pb:建物產權空間>
              </cityObjectMember>
            </CityModel>
            """;

        var result = service.ProcessXml(xml);

        var building = Assert.Single(result);
        Assert.All(building.Coordinates.SelectMany(polygon => polygon), point => Assert.Equal(0, point[2]));
        Assert.DoesNotContain("座標 JSON 格式解析失敗", building.ErrorMessages);
    }

    [Fact]
    public void ProcessXml_CityGmlWithExplicitSrs_TransformsProjectedCoordinatesToWgs84()
    {
        var service = CreateService();
        var xml = """
            <CityModel xmlns="http://www.opengis.net/citygml/2.0"
                       xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
                       xmlns:gml="http://www.opengis.net/gml"
                       xmlns:pb="https://land.moi.gov.tw/schema/propertybuilding">
              <cityObjectMember>
                <pb:建物產權空間 gml:id="id-explicit">
                  <bldg:boundedBy>
                    <bldg:WallSurface>
                      <bldg:lod2MultiSurface>
                        <gml:MultiSurface>
                          <gml:surfaceMember>
                            <gml:Polygon srsName="EPSG:3826">
                              <gml:exterior>
                                <gml:LinearRing>
                                  <gml:posList>251582.6776065464 2742712.2278096904 0.00 251587.1797726064 2742721.7136149006 0.00 251582.66272250641 2742723.8575035008 0.00 251578.1605564464 2742714.3716982906 0.00 251582.6776065464 2742712.2278096904 0.00</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Polygon>
                          </gml:surfaceMember>
                        </gml:MultiSurface>
                      </bldg:lod2MultiSurface>
                    </bldg:WallSurface>
                  </bldg:boundedBy>
                  <pb:建號母號>X001</pb:建號母號>
                  <pb:層次>001</pb:層次>
                </pb:建物產權空間>
              </cityObjectMember>
            </CityModel>
            """;

        var result = service.ProcessXml(xml);

        var building = Assert.Single(result);
        Assert.All(building.Coordinates.SelectMany(polygon => polygon), point =>
        {
            Assert.InRange(point[0], 119d, 123d);
            Assert.InRange(point[1], 21d, 26d);
        });
        Assert.DoesNotContain(building.FixMessages, message => message.Contains("座標系統推定"));
    }

    [Fact]
    public void ProcessXml_GeographicCoordinates_RemainUnchanged()
    {
        var service = CreateService();
        var xml = """
            <CityModel xmlns="http://www.opengis.net/citygml/2.0"
                       xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
                       xmlns:gml="http://www.opengis.net/gml"
                       xmlns:pb="https://land.moi.gov.tw/schema/propertybuilding">
              <cityObjectMember>
                <pb:建物產權空間 gml:id="geo-id">
                  <bldg:boundedBy>
                    <bldg:WallSurface>
                      <bldg:lod2MultiSurface>
                        <gml:MultiSurface>
                          <gml:surfaceMember>
                            <gml:Polygon srsName="urn:ogc:def:crs:OGC:1.3:CRS84">
                              <gml:exterior>
                                <gml:LinearRing>
                                  <gml:posList>121.5601 25.0331 10 121.5602 25.0331 10 121.5602 25.0332 14 121.5601 25.0332 14 121.5601 25.0331 10</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Polygon>
                          </gml:surfaceMember>
                        </gml:MultiSurface>
                      </bldg:lod2MultiSurface>
                    </bldg:WallSurface>
                  </bldg:boundedBy>
                  <pb:建號母號>GEO1</pb:建號母號>
                  <pb:層次>003</pb:層次>
                </pb:建物產權空間>
              </cityObjectMember>
            </CityModel>
            """;

        var result = service.ProcessXml(xml);

        var point = Assert.Single(result).Coordinates[0][0];
        Assert.Equal(121.5601, point[0], 4);
        Assert.Equal(25.0331, point[1], 4);
        Assert.Equal(10, point[2], 4);
    }

    [Fact]
    public void ProcessJson_GeoJsonFeatureCollection_StillInflatesFlatFloor()
    {
        var service = CreateService();
        var json = """
            {
              "type": "FeatureCollection",
              "features": [
                {
                  "type": "Feature",
                  "properties": {
                    "MID": "mid-1",
                    "OID": "oid-1",
                    "建號母號": "G001",
                    "層次": "001",
                    "高度": "13.2"
                  },
                  "geometry": {
                    "type": "Polygon",
                    "coordinates": [
                      [[0,0,10],[2,0,10],[2,2,10],[0,2,10],[0,0,10]]
                    ]
                  }
                }
              ]
            }
            """;

        var result = service.ProcessJson(json);

        var building = Assert.Single(result);
        Assert.Equal("mid-1", building.Mid);
        Assert.True(building.Coordinates.Count >= 3);
        Assert.Equal(10, building.MinHeight);
        Assert.Equal(13.2, building.MaxHeight);
        Assert.True(building.IsFixed);
        Assert.Contains(building.FixMessages, message => message.Contains("GeoJSON 修復"));
    }

    [Fact]
    public void ProcessXml_NonCityGml_PreprocessorBypass_PreservesExistingPath()
    {
        var service = CreateService(new StubXmlImportPreprocessor(xml => XmlImportPreprocessResult.Passthrough(
            xml,
            new CityGmlDetectionResult
            {
                IsCityGml = false,
                HasTopologyRelevantGeometry = false,
            })));

        var xml = """
            <ArrayOfConsistsOfBuildingPart>
              <ConsistsOfBuildingPart>
                <MID>1001</MID>
                <OID>2002</OID>
                <建號母號>A001</建號母號>
                <層次>001</層次>
                <boundedBy>[[[0,0,0],[4,0,0],[4,4,3.2],[0,4,3.2],[0,0,0]]]</boundedBy>
              </ConsistsOfBuildingPart>
            </ArrayOfConsistsOfBuildingPart>
            """;

        var result = service.ProcessXml(xml);

        var building = Assert.Single(result);
        Assert.Equal("A001", building.BuildingNo);
        Assert.False(building.IsFixed);
        Assert.DoesNotContain(building.FixMessages, message => message.Contains("CityGML"));
    }

    [Fact]
    public void ProcessXml_CityGmlPreprocessorSuccess_UsesRepairedXmlAndAddsFixMessage()
    {
        var repairedXml = """
            <CityModel xmlns="http://www.opengis.net/citygml/2.0"
                       xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
                       xmlns:gml="http://www.opengis.net/gml"
                       xmlns:pb="https://land.moi.gov.tw/schema/propertybuilding">
              <cityObjectMember>
                <pb:建物產權空間 gml:id="fixed-id">
                  <bldg:boundedBy>
                    <bldg:WallSurface>
                      <bldg:lod2MultiSurface>
                        <gml:MultiSurface>
                          <gml:surfaceMember>
                            <gml:Polygon>
                              <gml:exterior>
                                <gml:LinearRing>
                                  <gml:posList>0 0 0 2 0 0 2 2 3 0 2 3 0 0 0</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Polygon>
                          </gml:surfaceMember>
                        </gml:MultiSurface>
                      </bldg:lod2MultiSurface>
                    </bldg:WallSurface>
                  </bldg:boundedBy>
                  <pb:建號母號>R001</pb:建號母號>
                  <pb:層次>002</pb:層次>
                </pb:建物產權空間>
              </cityObjectMember>
            </CityModel>
            """;

        var service = CreateService(new StubXmlImportPreprocessor(_ => new XmlImportPreprocessResult
        {
            XmlContent = repairedXml,
            Detection = new CityGmlDetectionResult
            {
                IsCityGml = true,
                HasTopologyRelevantGeometry = true,
            },
            AttemptedPreprocess = true,
            RepairApplied = true,
            Messages = ["CityDoctor2 已完成 CityGML 拓撲預處理。"],
        }));

        var result = service.ProcessXml("<ignored />");

        var building = Assert.Single(result);
        Assert.Equal("fixed-id", building.Mid);
        Assert.Equal("R001", building.BuildingNo);
        Assert.True(building.IsFixed);
        Assert.Contains(building.FixMessages, message => message.Contains("CityGML 拓撲預處理"));
        Assert.Equal(0, building.MinHeight);
        Assert.Equal(3, building.MaxHeight);
    }

    [Fact]
    public void ProcessXml_CityGmlPreprocessorFailure_FallsBackToOriginalXml()
    {
        var originalXml = """
            <CityModel xmlns="http://www.opengis.net/citygml/2.0"
                       xmlns:bldg="http://www.opengis.net/citygml/building/2.0"
                       xmlns:gml="http://www.opengis.net/gml"
                       xmlns:pb="https://land.moi.gov.tw/schema/propertybuilding">
              <cityObjectMember>
                <pb:建物產權空間 gml:id="fallback-id">
                  <bldg:boundedBy>
                    <bldg:WallSurface>
                      <bldg:lod2MultiSurface>
                        <gml:MultiSurface>
                          <gml:surfaceMember>
                            <gml:Polygon>
                              <gml:exterior>
                                <gml:LinearRing>
                                  <gml:posList>0 0 0 2 0 0 2 2 2 0 2 2 0 0 0</gml:posList>
                                </gml:LinearRing>
                              </gml:exterior>
                            </gml:Polygon>
                          </gml:surfaceMember>
                        </gml:MultiSurface>
                      </bldg:lod2MultiSurface>
                    </bldg:WallSurface>
                  </bldg:boundedBy>
                  <pb:建號母號>F001</pb:建號母號>
                  <pb:層次>001</pb:層次>
                </pb:建物產權空間>
              </cityObjectMember>
            </CityModel>
            """;

        var service = CreateService(new StubXmlImportPreprocessor(xml => XmlImportPreprocessResult.Passthrough(
            xml,
            new CityGmlDetectionResult
            {
                IsCityGml = true,
                HasTopologyRelevantGeometry = true,
            },
            ["CityDoctor2 執行失敗，已回退至原始 CityGML 匯入流程。"])));

        var result = service.ProcessXml(originalXml);

        var building = Assert.Single(result);
        Assert.Equal("fallback-id", building.Mid);
        Assert.Equal("F001", building.BuildingNo);
        Assert.False(building.IsAbnormal);
        Assert.Contains(building.FixMessages, message => message.Contains("CityGML 匯入提示"));
    }

    private static BuildingProcessorService CreateService(IXmlImportPreprocessor? preprocessor = null)
    {
        return new BuildingProcessorService(
            Microsoft.Extensions.Options.Options.Create(DefaultDetection),
            preprocessor ?? new StubXmlImportPreprocessor(xml => XmlImportPreprocessResult.Passthrough(xml)),
            new CoordinateTransformService());
    }

    private sealed class StubXmlImportPreprocessor(Func<string, XmlImportPreprocessResult> callback) : IXmlImportPreprocessor
    {
        public XmlImportPreprocessResult Preprocess(string xmlContent) => callback(xmlContent);
    }
}
