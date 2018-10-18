﻿using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class Water : MonoBehaviour
{
	[SerializeField]
	bool disablePixelLights = true;
	
	[SerializeField][Range(1, 5)]
	int reflectionDownsample = 2;
	
	[SerializeField]
	float clipPlaneOffset = 0.2f; // this is actually a maximum offset
 
	[SerializeField]
	LayerMask m_ReflectLayers = -1;

	[SerializeField]
	WaterSettings settings = WaterSettings.Default;
	public WaterSettings Settings => settings;

	[SerializeField]
	Material surfaceMaterial;
	
	[SerializeField]
	Material groundMaterial;

	Camera reflectionCamera;
	Dictionary<Camera, RenderTexture> reflTextures = new Dictionary<Camera, RenderTexture>();
	Vector2Int currentTextureDimensions;

	static bool insideRendering;
 
	void OnWillRenderObject()
	{
		if(!surfaceMaterial)
			return;
		
		UpdateMaterial();
		
		var rend = GetComponent<Renderer>();
		
		if(!enabled || !rend || !rend.sharedMaterial || !rend.enabled)
			return;
 
		Camera cam = Camera.current;
		
		if(!cam)
			return;
 
		// safeguard from recursive reflections.        
		if(insideRendering)
			return;
		
		insideRendering = true;
 
		var reflTexture = GetRenderTexture(cam);
		surfaceMaterial.SetTexture("_ReflectionTex", reflTexture);
 
		// find out the reflection plane: position and normal in world space
		Vector3 pos = transform.position;
		Vector3 normal = cam.transform.position.y > pos.y ? Vector3.up : Vector3.down;
		
		// Optionally disable pixel lights for reflection
		int oldPixelLightCount = QualitySettings.pixelLightCount;
		
		if(disablePixelLights)
			QualitySettings.pixelLightCount = 0;
 
		UpdateCameraModes(cam);
 
		// Render reflection
		// Reflect camera around reflection plane
		float d = -Vector3.Dot(normal, pos);
		Vector4 reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);
 
		Matrix4x4 reflection = Matrix4x4.zero;
		CalculateReflectionMatrix(ref reflection, reflectionPlane);
		Vector3 oldPos = cam.transform.position;
		Vector3 newPos = reflection.MultiplyPoint(oldPos);
		reflectionCamera.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;
 
		// Set up oblique projection matrix so that near plane is our reflection
		// plane. This way we clip everything below/above it for free.
		Vector4 clipPlane = CameraSpacePlane(reflectionCamera, pos, normal);
		Matrix4x4 projection = cam.CalculateObliqueMatrix(clipPlane);
		reflectionCamera.projectionMatrix = projection;
 
		reflectionCamera.cullingMatrix = cam.projectionMatrix * cam.worldToCameraMatrix;
		reflectionCamera.cullingMask = ~(1 << 4) & m_ReflectLayers.value; // never render water layer

		var oldCulling = GL.invertCulling;
		GL.invertCulling = !oldCulling;
		reflectionCamera.transform.position = newPos;
		Vector3 euler = cam.transform.eulerAngles;
		reflectionCamera.transform.eulerAngles = new Vector3(-euler.x, euler.y, euler.z);
		
		reflectionCamera.targetTexture = reflTexture;
		reflectionCamera.Render();
		reflectionCamera.targetTexture = null;
		
		reflectionCamera.transform.position = oldPos;
		GL.invertCulling = oldCulling;
 
		// Restore pixel light count
		if(disablePixelLights)
			QualitySettings.pixelLightCount = oldPixelLightCount;
 
		insideRendering = false;
	}

	void UpdateMaterial()
	{
		if(surfaceMaterial)
		{
			surfaceMaterial.SetTexture("_NormalTex", settings.normalTexture);
			surfaceMaterial.SetVector("_WindDirection", settings.windDirection);
			surfaceMaterial.SetFloat("_WindSpeed", settings.windSpeed);
			surfaceMaterial.SetFloat("_Visibility", settings.visibility);
			surfaceMaterial.SetFloat("_WaveScale", settings.waveScale);
			surfaceMaterial.SetFloat("_ScatterAmount", settings.scatterAmount);
			surfaceMaterial.SetColor("_ScatterColor", settings.scatterColor);
			surfaceMaterial.SetFloat("_ReflDistortionAmount", settings.reflectionDistortionAmount);
			surfaceMaterial.SetFloat("_RefrDistortionAmount", settings.refractionDistortionAmount);
			surfaceMaterial.SetFloat("_AberrationAmount", settings.aberrationAmount);
			surfaceMaterial.SetColor("_WaterExtinction", settings.waterExtinction);
			surfaceMaterial.SetColor("_SunExtinction", settings.sunExtinction);
		}

		if(groundMaterial)
		{
			groundMaterial.SetTexture("_NormalTex", settings.normalTexture);
			groundMaterial.SetVector("_WindDirection", settings.windDirection);
			groundMaterial.SetFloat("_WindSpeed", settings.windSpeed);
			groundMaterial.SetFloat("_Visibility", settings.visibility);
			groundMaterial.SetFloat("_WaveScale", settings.waveScale);
			groundMaterial.SetColor("_MudExtinction", settings.waterExtinction);
			groundMaterial.SetColor("_WaterExtinction", settings.waterExtinction);
			groundMaterial.SetColor("_SunExtinction", settings.sunExtinction);
		}
	}

	void OnEnable()
	{
		Camera.onPreRender += PreRender;
	}

	void PreRender(Camera c)
	{
		// only update _WorldSpaceCameraPos2 if camera is not a reflection camera
		// we do it here because it applies to underwater, even if surface isn't rendered
		if(groundMaterial && c != reflectionCamera)
			groundMaterial.SetVector("_WorldSpaceCameraPos2", c.transform.position);
	}
 
	void OnDisable()
	{
		foreach(var kvp in reflTextures)
			if(kvp.Value)
				DestroyImmediate(kvp.Value);

		reflTextures.Clear();

		if(reflectionCamera)
		{
			DestroyImmediate(reflectionCamera.gameObject);
			reflectionCamera = null;
		}

		Camera.onPreRender -= PreRender;
	}

	void UpdateCameraModes(Camera src)
	{
		if(reflectionCamera == null)
		{
			reflectionCamera = new GameObject("Reflection Camera", typeof(Camera)).GetComponent<Camera>();
			reflectionCamera.enabled = false;
			reflectionCamera.gameObject.hideFlags = HideFlags.DontSave;
		}
		
		reflectionCamera.clearFlags = src.clearFlags;
		reflectionCamera.backgroundColor = src.backgroundColor;        
		
		if(src.clearFlags == CameraClearFlags.Skybox)
		{
			Skybox sky = src.GetComponent<Skybox>();
			Skybox reflSky = reflectionCamera.GetComponent<Skybox>();
			
			if(!sky || !sky.material)
			{
				if(reflSky)
					reflSky.enabled = false;
			}
			else
			{
				if(!reflSky)
					reflSky = reflectionCamera.gameObject.AddComponent<Skybox>();
				
				reflSky.enabled = true;
				reflSky.material = sky.material;
			}
		}
		
		reflectionCamera.farClipPlane = src.farClipPlane;
		reflectionCamera.nearClipPlane = src.nearClipPlane;
		reflectionCamera.orthographic = src.orthographic;
		reflectionCamera.fieldOfView = src.fieldOfView;
		reflectionCamera.aspect = src.aspect;
		reflectionCamera.orthographicSize = src.orthographicSize;
	}
 
	RenderTexture GetRenderTexture(Camera currentCamera)
	{
		Vector2Int newTextureDimensions = new Vector2Int(currentCamera.scaledPixelWidth / reflectionDownsample, currentCamera.scaledPixelHeight / reflectionDownsample);
		RenderTexture rt;
		
		if(reflTextures.TryGetValue(currentCamera, out rt))
		{
			if(rt.width != newTextureDimensions.x || rt.height != newTextureDimensions.y)
			{
				DestroyImmediate(rt);
				reflTextures.Remove(currentCamera);
			}

			goto Skip;
		}
		
		rt = new RenderTexture(newTextureDimensions.x, newTextureDimensions.y, 16, RenderTextureFormat.DefaultHDR)
		{
			name = $"PlanarReflection {GetInstanceID()}",
			hideFlags = HideFlags.DontSave
		};
		
		reflTextures.Add(currentCamera, rt);
		
		Skip:
		
		return rt;
	}
 
	Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal)
	{
		// lerp between small clip plane offset and maximum offset based on dist from water plane
		// we want small offset when near surface and large offset when high above surface
		float dist = Mathf.Abs(cam.transform.position.y - pos.y);
		float clipOffset = Mathf.Lerp(.01f, clipPlaneOffset, Mathf.Clamp01(dist));
		
		Matrix4x4 m = cam.worldToCameraMatrix;
		pos = m.MultiplyPoint(pos - normal * clipOffset);
		normal = m.MultiplyVector(normal).normalized;
		
		return new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(pos, normal));
	}
 
	static void CalculateReflectionMatrix(ref Matrix4x4 reflectionMat, Vector4 plane)
	{
		reflectionMat.m00 = 1F - 2F * plane[0] * plane[0];
		reflectionMat.m01 = -2F * plane[0] * plane[1];
		reflectionMat.m02 = -2F * plane[0] * plane[2];
		reflectionMat.m03 = -2F * plane[3] * plane[0];

		reflectionMat.m10 = -2F * plane[1] * plane[0];
		reflectionMat.m11 = 1F - 2F * plane[1] * plane[1];
		reflectionMat.m12 = -2F * plane[1] * plane[2];
		reflectionMat.m13 = -2F * plane[3] * plane[1];

		reflectionMat.m20 = -2F * plane[2] * plane[0];
		reflectionMat.m21 = -2F * plane[2] * plane[1];
		reflectionMat.m22 = 1F - 2F * plane[2] * plane[2];
		reflectionMat.m23 = -2F * plane[3] * plane[2];
 
		reflectionMat.m30 = 0F;
		reflectionMat.m31 = 0F;
		reflectionMat.m32 = 0F;
		reflectionMat.m33 = 1F;
	}
}