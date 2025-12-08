//using Microsoft.Extensions.Logging;
//using NUnit.Framework;
//using Moq;
//using Newtonsoft.Json.Linq;
//using UMCPServer.Tools;
//using UMCPServer.Services;
//using System.Threading.Tasks;

//namespace UMCPServer.Tests.IntegrationTests.Tools
//{
//    /// <summary>
//    /// Integration tests for ManageAssetTool functionality.
//    /// Tests server-side asset management operations with Unity connection.
//    /// </summary>
//    [TestFixture]
//    public class ManageAssetToolTestsMock
//    {
//        private Mock<ILogger<ManageAssetTool>> _mockLogger;
//        private Mock<UnityConnectionService> _mockUnityConnection;
//        private ManageAssetTool _manageAssetTool;

//        [SetUp]
//        public void SetUp()
//        {
//            _mockLogger = new Mock<ILogger<ManageAssetTool>>();
//            _mockUnityConnection = new Mock<UnityConnectionService>();
//            _manageAssetTool = new ManageAssetTool(_mockLogger.Object, _mockUnityConnection.Object);
//        }

//        #region Parameter Validation Tests

//        [Test]
//        [Description("Tests that ManageAsset returns error for missing action parameter")]
//        public async Task ManageAsset_ReturnsError_ForMissingAction()
//        {
//            // Act
//            var result = await _manageAssetTool.ManageAsset(
//                action: "",
//                path: "Assets/TestAsset.mat");

//            // Assert
//            Assert.That(result, Is.Not.Null);
//            var resultObj = JObject.FromObject(result);
//            Assert.That(resultObj["success"]?.ToObject<bool>(), Is.False, "Should return failure for missing action");
//            Assert.That(resultObj["error"]?.ToString().Contains("action"), Is.True, "Error should mention action parameter");
//        }

//        [Test]
//        [Description("Tests that ManageAsset returns error for invalid action")]
//        public async Task ManageAsset_ReturnsError_ForInvalidAction()
//        {
//            // Act
//            var result = await _manageAssetTool.ManageAsset(
//                action: "invalid_action",
//                path: "Assets/TestAsset.mat");

//            // Assert
//            Assert.That(result, Is.Not.Null);
//            var resultObj = JObject.FromObject(result);
//            Assert.That(resultObj["success"]?.ToObject<bool>(), Is.False, "Should return failure for invalid action");
//            Assert.That(resultObj["error"]?.ToString().Contains("Invalid action"), Is.True, "Error should mention invalid action");
//        }

//        [Test]
//        [Description("Tests that ManageAsset validates required parameters for create action")]
//        public async Task ManageAsset_ValidatesRequiredParameters_ForCreateAction()
//        {
//            // Test missing path
//            var resultMissingPath = await _manageAssetTool.ManageAsset(
//                action: "create",
//                assetType: "material");

//            var resultObjMissingPath = JObject.FromObject(resultMissingPath);
//            Assert.That(resultObjMissingPath["success"]?.ToObject<bool>(), Is.False, "Should return failure for missing path");
//            Assert.That(resultObjMissingPath["error"]?.ToString().Contains("path"), Is.True, "Error should mention path parameter");

//            // Test missing assetType
//            var resultMissingType = await _manageAssetTool.ManageAsset(
//                action: "create",
//                path: "Assets/TestAsset.mat");

//            var resultObjMissingType = JObject.FromObject(resultMissingType);
//            Assert.That(resultObjMissingType["success"]?.ToObject<bool>(), Is.False, "Should return failure for missing assetType");
//            Assert.That(resultObjMissingType["error"]?.ToString().Contains("assetType"), Is.True, "Error should mention assetType parameter");
//        }

//        [Test]
//        [Description("Tests that ManageAsset validates required parameters for move/rename actions")]
//        public async Task ManageAsset_ValidatesRequiredParameters_ForMoveRenameActions()
//        {
//            // Test move action
//            var resultMove = await _manageAssetTool.ManageAsset(
//                action: "move",
//                path: "Assets/TestAsset.mat");

//            var resultObjMove = JObject.FromObject(resultMove);
//            Assert.That(resultObjMove["success"]?.ToObject<bool>(), Is.False, "Should return failure for missing destination");
//            Assert.That(resultObjMove["error"]?.ToString().Contains("destination"), Is.True, "Error should mention destination parameter");

//            // Test rename action
//            var resultRename = await _manageAssetTool.ManageAsset(
//                action: "rename",
//                path: "Assets/TestAsset.mat");

//            var resultObjRename = JObject.FromObject(resultRename);
//            Assert.That(resultObjRename["success"]?.ToObject<bool>(), Is.False, "Should return failure for missing destination");
//            Assert.That(resultObjRename["error"]?.ToString().Contains("destination"), Is.True, "Error should mention destination parameter");
//        }

//        [Test]
//        [Description("Tests that ManageAsset validates path for path-required actions")]
//        public async Task ManageAsset_ValidatesPath_ForPathRequiredActions()
//        {
//            string[] pathRequiredActions = { "modify", "delete", "duplicate", "get_info", "create_folder", "get_components", "import" };

//            foreach (string action in pathRequiredActions)
//            {
//                var result = await _manageAssetTool.ManageAsset(action: action);
//                var resultObj = JObject.FromObject(result);
                
//                Assert.That(resultObj["success"]?.ToObject<bool>(), Is.False, $"Should return failure for missing path in {action} action");
//                Assert.That(resultObj["error"]?.ToString().Contains("path"), Is.True, $"Error should mention path parameter for {action} action");
//            }
//        }

//        #endregion

//        #region Unity Connection Tests

//        [Test]
//        [Description("Tests that ManageAsset returns error when Unity is not connected")]
//        public async Task ManageAsset_ReturnsError_WhenUnityNotConnected()
//        {
//            // Arrange
//            _mockUnityConnection.Setup(x => x.IsConnected).Returns(false);
//            _mockUnityConnection.Setup(x => x.ConnectAsync()).ReturnsAsync(false);

//            // Act
//            var result = await _manageAssetTool.ManageAsset(
//                action: "create",
//                path: "Assets/TestAsset.mat",
//                assetType: "material");

//            // Assert
//            Assert.That(result, Is.Not.Null);
//            var resultObj = JObject.FromObject(result);
//            Assert.That(resultObj["success"]?.ToObject<bool>(), Is.False, "Should return failure when Unity not connected");
//            Assert.That(resultObj["error"]?.ToString().Contains("Unity Editor"), Is.True, "Error should mention Unity Editor");
//        }

//        [Test]
//        [Description("Tests that ManageAsset successfully connects to Unity when initially disconnected")]
//        public async Task ManageAsset_ConnectsToUnity_WhenInitiallyDisconnected()
//        {
//            // Arrange
//            _mockUnityConnection.Setup(x => x.IsConnected).Returns(false);
//            _mockUnityConnection.Setup(x => x.ConnectAsync()).ReturnsAsync(true);
//            _mockUnityConnection.Setup(x => x.SendCommandAsync("manage_asset", It.IsAny<JObject>(), default, It.Is<bool>(b => b == false)))
//                .ReturnsAsync(new JObject
//                {
//                    ["status"] = "success",
//                    ["result"] = new JObject { ["message"] = "Asset created successfully" }
//                });

//            // Act
//            var result = await _manageAssetTool.ManageAsset(
//                action: "create",
//                path: "Assets/TestAsset.mat",
//                assetType: "material");

//            // Assert
//            Assert.That(result, Is.Not.Null);
//            var resultObj = JObject.FromObject(result);
//            Assert.That(resultObj["success"]?.ToObject<bool>(), Is.True, "Should succeed after connecting to Unity");
            
//            // Verify ConnectAsync was called
//            _mockUnityConnection.Verify(x => x.ConnectAsync(), Times.Once);
//        }

//        #endregion

//        #region Parameter Building Tests

//        [Test]
//        [Description("Tests that ManageAsset builds correct parameters for Unity")]
//        public async Task ManageAsset_BuildsCorrectParameters_ForUnity()
//        {
//            // Arrange
//            _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
            
//            JObject capturedParameters = null;
//            _mockUnityConnection.Setup(x => x.SendCommandAsync("manage_asset", It.IsAny<JObject>(), default,
//                It.Is<bool>(b => b == false)))
//                .Callback<string, JObject, System.Threading.CancellationToken>((cmd, param, token) => capturedParameters = param)
//                .ReturnsAsync(new JObject
//                {
//                    ["status"] = "success",
//                    ["result"] = new JObject { ["message"] = "Success" }
//                });

//            // Act
//            await _manageAssetTool.ManageAsset(
//                action: "create",
//                path: "Assets/TestAsset.mat",
//                assetType: "material",
//                properties: new { shader = "Standard" },
//                generatePreview: true);

//            // Assert
//            Assert.That(capturedParameters, Is.Not.Null, "Parameters should be captured");
//            Assert.That(capturedParameters?["action"]?.ToString(), Is.EqualTo("create"));
//            Assert.That(capturedParameters["path"]?.ToString(), Is.EqualTo("Assets/TestAsset.mat"));
//            Assert.That(capturedParameters["assetType"]?.ToString(), Is.EqualTo("material"));
//            Assert.That(capturedParameters["properties"], Is.Not.Null, "Properties should be included");
//            Assert.That(capturedParameters["generatePreview"]?.ToObject<bool>(), Is.True, "GeneratePreview should be true");
//        }

//        [Test]
//        [Description("Tests that ManageAsset builds correct search parameters")]
//        public async Task ManageAsset_BuildsCorrectSearchParameters()
//        {
//            // Arrange
//            _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
            
//            JObject capturedParameters = null;
//            _mockUnityConnection.Setup(x => x.SendCommandAsync("manage_asset", It.IsAny<JObject>(), default, It.Is<bool>(b => b == false)))
//                .Callback<string, JObject, System.Threading.CancellationToken>((cmd, param, token) => capturedParameters = param)
//                .ReturnsAsync(new JObject
//                {
//                    ["status"] = "success",
//                    ["result"] = new JObject 
//                    { 
//                        ["totalAssets"] = 10,
//                        ["pageSize"] = 25,
//                        ["pageNumber"] = 2,
//                        ["assets"] = new JArray()
//                    }
//                });

//            // Act
//            await _manageAssetTool.ManageAsset(
//                action: "search",
//                searchPattern: "test",
//                filterType: "Material",
//                filterDateAfter: "2023-01-01T00:00:00Z",
//                pageSize: 25,
//                pageNumber: 2);

//            // Assert
//            Assert.That(capturedParameters, Is.Not.Null, "Parameters should be captured");
//            Assert.That(capturedParameters["action"]?.ToString(), Is.EqualTo("search"));
//            Assert.That(capturedParameters["searchPattern"]?.ToString(), Is.EqualTo("test"));
//            Assert.That(capturedParameters["filterType"]?.ToString(), Is.EqualTo("Material"));
//            Assert.That(capturedParameters["filterDateAfter"]?.ToString(), Is.EqualTo("2023-01-01T00:00:00Z"));
//            Assert.That(capturedParameters["pageSize"]?.ToObject<int>(), Is.EqualTo(25));
//            Assert.That(capturedParameters["pageNumber"]?.ToObject<int>(), Is.EqualTo(2));
//        }

//        #endregion

//        #region Response Handling Tests

//        [Test]
//        [Description("Tests handling of successful create response")]
//        public async Task ManageAsset_HandlesCreateResponse_Successfully()
//        {
//            // Arrange
//            _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
//            _mockUnityConnection.Setup(x => x.SendCommandAsync("manage_asset", It.IsAny<JObject>(), default, It.Is<bool>(b => b == false)))
//                .ReturnsAsync(new JObject
//                {
//                    ["status"] = "success",
//                    ["result"] = new JObject 
//                    { 
//                        ["message"] = "Asset created successfully",
//                        ["path"] = "Assets/TestAsset.mat"
//                    }
//                });

//            // Act
//            var result = await _manageAssetTool.ManageAsset(
//                action: "create",
//                path: "Assets/TestAsset.mat",
//                assetType: "material");

//            // Assert
//            Assert.That(result, Is.Not.Null);
//            var resultObj = JObject.FromObject(result);
//            Assert.That(resultObj["success"]?.ToObject<bool>(), Is.True, "Should return success");
//            Assert.That(resultObj["message"]?.ToString(), Is.EqualTo("Asset created successfully"));
//            Assert.That(resultObj["path"]?.ToString(), Is.EqualTo("Assets/TestAsset.mat"));
//        }

//        [Test]
//        [Description("Tests handling of successful search response")]
//        public async Task ManageAsset_HandlesSearchResponse_Successfully()
//        {
//            // Arrange
//            _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
//            _mockUnityConnection.Setup(x => x.SendCommandAsync("manage_asset", It.IsAny<JObject>(), default,
//                It.Is<bool>(b => b == false)))
//                .ReturnsAsync(new JObject
//                {
//                    ["status"] = "success",
//                    ["result"] = new JObject 
//                    { 
//                        ["totalAssets"] = 15,
//                        ["pageSize"] = 10,
//                        ["pageNumber"] = 1,
//                        ["assets"] = new JArray
//                        {
//                            new JObject { ["path"] = "Assets/Asset1.mat" },
//                            new JObject { ["path"] = "Assets/Asset2.mat" }
//                        }
//                    }
//                });

//            // Act
//            var result = await _manageAssetTool.ManageAsset(action: "search", searchPattern: "test");

//            // Assert
//            Assert.That(result, Is.Not.Null);
//            var resultObj = JObject.FromObject(result);
//            Assert.That(resultObj["success"]?.ToObject<bool>(), Is.True, "Should return success");
//            Assert.That(resultObj["totalAssets"]?.ToObject<int>(), Is.EqualTo(15));
//            Assert.That(resultObj["pageSize"]?.ToObject<int>(), Is.EqualTo(10));
//            Assert.That(resultObj["pageNumber"]?.ToObject<int>(), Is.EqualTo(1));
            
//            var assets = resultObj["assets"] as JArray;
//            Assert.That(assets, Is.Not.Null, "Should include assets array");
//            Assert.That(assets.Count, Is.EqualTo(2), "Should include correct number of assets");
//        }

//        [Test]
//        [Description("Tests handling of Unity error response")]
//        public async Task ManageAsset_HandlesUnityError_Correctly()
//        {
//            // Arrange
//            _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
//            _mockUnityConnection.Setup(x => x.SendCommandAsync("manage_asset", It.IsAny<JObject>(), default, It.Is<bool>(b => b == false)))
//                .ReturnsAsync(new JObject
//                {
//                    ["status"] = "error",
//                    ["error"] = "Asset not found"
//                });

//            // Act
//            var result = await _manageAssetTool.ManageAsset(
//                action: "delete",
//                path: "Assets/NonExistent.mat");

//            // Assert
//            Assert.That(result, Is.Not.Null);
//            var resultObj = JObject.FromObject(result);
//            Assert.That(resultObj["success"]?.ToObject<bool>(), Is.False, "Should return failure for Unity error");
//            Assert.That(resultObj["error"]?.ToString(), Is.EqualTo("Asset not found"));
//        }

//        [Test]
//        [Description("Tests handling of Unity timeout")]
//        public async Task ManageAsset_HandlesTimeout_Correctly()
//        {
//            // Arrange
//            _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
//            _mockUnityConnection.Setup(x => x.SendCommandAsync("manage_asset", It.IsAny<JObject>(), default, It.Is<bool>(b => b == false)))
//                .ReturnsAsync((JObject)null); // Simulate timeout

//            // Act
//            var result = await _manageAssetTool.ManageAsset(
//                action: "create",
//                path: "Assets/TestAsset.mat",
//                assetType: "material");

//            // Assert
//            Assert.That(result, Is.Not.Null);
//            var resultObj = JObject.FromObject(result);
//            Assert.That(resultObj["success"]?.ToObject<bool>(), Is.False, "Should return failure for timeout");
//            Assert.That(resultObj["error"]?.ToString().Contains("timeout"), Is.True, "Error should mention timeout");
//        }

//        #endregion

//        #region Properties Handling Tests

//        [Test]
//        [Description("Tests that properties are correctly parsed from string JSON")]
//        public async Task ManageAsset_ParsesStringProperties_Correctly()
//        {
//            // Arrange
//            _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
            
//            JObject capturedParameters = null;
//            _mockUnityConnection.Setup(x => x.SendCommandAsync("manage_asset", It.IsAny<JObject>(), default, It.Is<bool>(b => b == false)))
//                .Callback<string, JObject, System.Threading.CancellationToken>((cmd, param, token) => capturedParameters = param)
//                .ReturnsAsync(new JObject { ["status"] = "success", ["result"] = new JObject() });

//            string jsonProperties = "{\"shader\":\"Standard\",\"color\":[1.0,0.0,0.0,1.0]}";

//            // Act
//            await _manageAssetTool.ManageAsset(
//                action: "create",
//                path: "Assets/TestAsset.mat",
//                assetType: "material",
//                properties: jsonProperties);

//            // Assert
//            Assert.That(capturedParameters, Is.Not.Null, "Parameters should be captured");
//            var properties = capturedParameters["properties"] as JObject;
//            Assert.That(properties, Is.Not.Null, "Properties should be parsed as JObject");
//            Assert.That(properties["shader"]?.ToString(), Is.EqualTo("Standard"));
//            Assert.That(properties["color"], Is.Not.Null, "Color property should be included");
//        }

//        [Test]
//        [Description("Tests that properties are handled as object when not JSON string")]
//        public async Task ManageAsset_HandlesObjectProperties_Correctly()
//        {
//            // Arrange
//            _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
            
//            JObject capturedParameters = null;
//            _mockUnityConnection.Setup(x => x.SendCommandAsync("manage_asset", It.IsAny<JObject>(), default, It.Is<bool>(b => b == false)))
//                .Callback<string, JObject, System.Threading.CancellationToken>((cmd, param, token) => capturedParameters = param)
//                .ReturnsAsync(new JObject { ["status"] = "success", ["result"] = new JObject() });

//            var objectProperties = new { shader = "Standard", metallic = 0.5f };

//            // Act
//            await _manageAssetTool.ManageAsset(
//                action: "create",
//                path: "Assets/TestAsset.mat",
//                assetType: "material",
//                properties: objectProperties);

//            // Assert
//            Assert.That(capturedParameters, Is.Not.Null, "Parameters should be captured");
//            var properties = capturedParameters["properties"];
//            Assert.That(properties, Is.Not.Null, "Properties should be included");
//            Assert.That(properties["shader"]?.ToString(), Is.EqualTo("Standard"));
//            Assert.That(properties["metallic"]?.ToObject<float>(), Is.EqualTo(0.5f));
//        }

//        #endregion

//        #region Action-Specific Response Tests

//        [Test]
//        [Description("Tests get_components response handling")]
//        public async Task ManageAsset_HandlesGetComponentsResponse_Successfully()
//        {
//            // Arrange
//            _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
//            _mockUnityConnection.Setup(x => x.SendCommandAsync("manage_asset", It.IsAny<JObject>(), default, It.Is<bool>(b => b == false)))
//                .ReturnsAsync(new JObject
//                {
//                    ["status"] = "success",
//                    ["result"] = new JArray
//                    {
//                        new JObject { ["typeName"] = "Transform", ["instanceID"] = 123 },
//                        new JObject { ["typeName"] = "MeshRenderer", ["instanceID"] = 456 }
//                    }
//                });

//            // Act
//            var result = await _manageAssetTool.ManageAsset(
//                action: "get_components",
//                path: "Assets/TestPrefab.prefab");

//            // Assert
//            Assert.That(result, Is.Not.Null);
//            var resultObj = JObject.FromObject(result);
//            Assert.That(resultObj["success"]?.ToObject<bool>(), Is.True, "Should return success");
            
//            var components = resultObj["components"] as JArray;
//            Assert.That(components, Is.Not.Null, "Should include components array");
//            Assert.That(components.Count, Is.EqualTo(2), "Should return correct number of components");
//            Assert.That(resultObj["message"]?.ToString(), Is.EqualTo("Found 2 component(s)"));
//        }

//        [Test]
//        [Description("Tests delete response handling")]
//        public async Task ManageAsset_HandlesDeleteResponse_Successfully()
//        {
//            // Arrange
//            _mockUnityConnection.Setup(x => x.IsConnected).Returns(true);
//            _mockUnityConnection.Setup(x => x.SendCommandAsync("manage_asset", It.IsAny<JObject>(), default, It.Is<bool>(b => b == false)))
//                .ReturnsAsync(new JObject
//                {
//                    ["status"] = "success",
//                    ["result"] = new JObject { ["message"] = "Asset deleted successfully" }
//                });

//            // Act
//            var result = await _manageAssetTool.ManageAsset(
//                action: "delete",
//                path: "Assets/TestAsset.mat");

//            // Assert
//            Assert.That(result, Is.Not.Null);
//            var resultObj = JObject.FromObject(result);
//            Assert.That(resultObj["success"]?.ToObject<bool>(), Is.True, "Should return success");
//            Assert.That(resultObj["message"]?.ToString(), Is.EqualTo("Asset deleted successfully"));
//        }

//        #endregion
//    }
//}