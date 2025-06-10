# Large Language Model Plugin TestConsole 

`Daenet.LLMPlugin.TestConsole` is a console library designed for the development and testing of Semantic Kernel plugins. While testing plugins is not a particularly complex task, it requires several repetitive steps such as loading plugins, configuring them, implementing a chat loop, maintaining message history, executing plugin functions, and more. Although these tasks are straightforward, they can become tedious when repeated frequently.

To streamline this process and accelerate development, we have created a library that simplifies the implementation of a testing console application. This console application can be used to quickly learn how to develop Semantic Kernel plugins and speed up the development process.

## Getting Started

To implement a plugin, follow these steps:

1. **Create a Console Application**: 
   Start by creating a new console application.
   
2. **Add the Package Reference**: 
   Add a reference to the package `Daenet.PluginTestConsole`.

3. **Implement Plugin Code**: 
   Implement the plugin-specific logic within your console application.

## Plugin Implementation

A plugin is a class, and the methods within the class represent the plugin's functions, which are invoked by a large language model (LLM). Each plugin class must have a corresponding configuration class. The configuration class must have the same name as the plugin class, suffixed with the word `Config`.

### Examples

- For a plugin class named `MyPlugin`, the configuration class should be named `MyPluginConfig`.
- Similarly, if you have a plugin named `AbcPlugin`, its configuration class should be named `AbcPluginConfig`.

This is not a strict requirement of the Semantic Kernel (SK), but it is considered a best practice to ensure that each plugin has an associated configuration class. This structure helps maintain clarity and modularity in the plugin design.

## Plugin Configuration

Plugin configuration is managed in the `appsettings.json` file. Configuration details for each plugin are specified in two places within this file. All plugins must be listed under the `Plugins` section, ensuring that the framework can load and initialize them properly.


~~~json
  "Plugins": [
    {
	  "Name": "Plugin1",
	  "AssemblyQualifiedName": "Daenet.GptBot.PluginLib.Cpdm.Plugin1, PluginAssemblyName1",
	  "JsonConfiguration": {
		"Prop1": "https://",
		"Prop2": "testkey"
	  }
	},
	{
	  "Name": "Plugin2",
	  "AssemblyQualifiedName": "Daenet.GptBot.PluginLib.Cpdm.Plugin2, PluginAssemblyName2",
	  "JsonConfiguration": {
		"Prop1": "https://",
		"Prop2": "testkey"
	  }
	}
  ],
~~~

The section *JsonConfiguration* must correspond to the configuration class of the plugin `MyPluginConfig`. The plugin configuration class must have properties that match the keys in the *JsonConfiguration* section. 
The plugin configuration class is used to deserialize the configuration data.

# How to implement plugin host?

To work with plugins, you need to implement the plugin and provide a host application.
Plugins are typically implemented in a library, which is referenced by the host application.
The project *Daenet.LLMPlugin.TestConsole.App* is a sample console application that demonstrates how to run the console with LLM functionality, which automatically loads your plugin.
It illustrates how to set up the plugin configuration in appsettings.json, and initialize the Test Console implemented in *Daenet.LLMPlugin.TestConsole*.

The sample application includes a built-in plugin implemented in the class MyPlugin.
This plugin provides simple functionality to retrieve information about running processes on the system.

The plugin configuration looks like this:

~~~json
  "Plugins": [
	{
	  "Name": "MyPlugin",
	  "AssemblyQualifiedName": "Daenet.LLMPlugin.TestConsole.App.MyPlugin, Daenet.LLMPlugin.TestConsole.App",
	  "JsonConfiguration": {
		"Prop1": "Value 1",
		"Prop2": "Value 2"
	  }
	}
  ],
~~~
  
 All plugins must be listed under the Plugins section to ensure that the framework can load and initialize them properly.
The plugin configuration must correspond to the plugin configuration class name, specified in the JsonConfiguration section of the plugin configuration.
The implementation of the *IPluginProvider* interface, as seen in the class *DefaultPluginProvider*, is responsible for loading the plugin.
The *DefaultPluginProvider* loads plugins defined in the 'Plugins' section, which are part of the AppDomain of the host application.
If plugins need to be loaded from another location dynamically, you can implement your own *IPluginProvider* and dynamically load plugins from the desired location (e.g., Blob Storage).

When the sample host application starts you can start using the 'MyPlugin'. Inside the console type following prompts:

`How many processes are currentlly running?`

`Is there any process that contains 'plugin' in the name of the proces?`

`Is the notepad running?`

`what is the id of the notepad process?`

`kill it`

`kill visual studio code`

`list first 50 processes`

`provide detailed process information`


# Test Console Plugin

The library **Daenet.LLMPlugin.TestConsole** provides several utility functions designed to enhance the development and testing of Semantic Kernel plugins. 
These functions are implemented as plugin `TestConsolePlugin`, which allows you to clear console output, manage conversation history, customize prompt appearances, list available plugins,
change promp colors etc. 
Following section outlines the functions available and provides example use cases for each.

## Function Overview

### 1. `ClearConsole`

- **Description**: Clears all text currently displayed in the console.
- **Use Case**: Useful when starting fresh, ensuring that no previous messages or clutter remain in the console.
  
Example:

`clear console`


### 2. `ClearMessageHistory`

- **Description**: Deletes all messages from the conversation history but does not affect the text displayed in the console.
- **Use Case**: Ideal for resetting the conversation history while retaining previous console logs for reference.

Examples:

`clear chat history`

`claer history`

`delete conversation`

### 3. `ListPlugins`

- **Description**: Retrieves a list of loaded plugins, with the option to display additional details for each plugin.
- **Use Case**: Helps developers quickly identify which plugins are available and, if required, fetch detailed information about each plugin.
- **Parameters**:
  - `details` (boolean, optional): If set to `true`, additional information about each plugin is displayed.

Examples:

`show loaded plugins`

`list plugins`

`provide detailed information for function ListPlugins`


### 4. `SetSystemPromptColor`

- **Description**: Customizes the color of the system prompt in the console.
- **Use Case**: Allows users to personalize the appearance of the system prompt for better readability or personal preference.
- **Parameters**:
  - `color` (string): Defines the desired color of the system prompt. Available options:
    - `"Black"`, `"DarkBlue"`, `"DarkGreen"`, `"DarkCyan"`, `"DarkRed"`, `"DarkMagenta"`, `"DarkYellow"`, `"Gray"`, `"DarkGray"`, `"Blue"`, `"Green"`, `"Cyan"`, `"Red"`, `"Magenta"`, `"Yellow"`, `"White"`.

Examples:

`set color of system prompt to cyan`


### 5. `SetUserPromptColor`

- **Description**: Changes the color of the user prompt/input.
- **Use Case**: Enables customization of the user prompt color for improved visibility or user preference.
- **Parameters**:
  - `color` (string): Defines the desired color for user input. The available options are the same as for `SetSystemPromptColor`.

Examples:

`set user prompt color to cyan`


### 6. `SetAssistantColor`

- **Description**: Adjusts the color of the assistant’s response messages in the console.
- **Use Case**: Distinguishes assistant messages from user inputs and system prompts through color customization.
- **Parameters**:
  - `color` (string): Specifies the desired color for assistant messages. Options are the same as those available for the system prompt.

Examples:

`set assistent output color to yellow`


### 7. `SetSystemPromptText`

- **Description**: Modifies the default text displayed in the system prompt.
- **Use Case**: Personalizes the system prompt text to meet specific testing or development needs.
- **Parameters**:
  - `promptText` (string): The custom text to be displayed as the system prompt.

Examples:

`set prompt to '=>'`

## Model Context Protocol (MCP) Support

The Plugin TestConsole also supports the MCP protocol, enabling the console to test tools hosted on an MCP server. This functionality allows developers to implement plugins as AI tools accessible via the MCP protocol.
To initialize a tool for use with the Plugin TestConsole, you must define it in the appsettings.json configuration file. 
The following example demonstrates how to configure two tools within the McpToolsConfig section:

~~~json
{

  "Plugins": [
  

  ],

  "McpToolsConfig": {
    "McpServers": [
      {
        "Name": "DAENET_TEST_MCP_SERVER",
        "Url": "https://localhost:7133/sse",
        "ApiKey": "54321-edcba-09876-jihgf"
      },
      {
        "Name": "Everything",
        "Command": "npx",
        "Arguments": "[\"-y\", \"@modelcontextprotocol/server-everything\"]"     
      }
    ]

  }
}

~~~

### Using Tools via SSE

The first tool, "DAENET_TEST_MCP_SERVER", is accessible via the MCP SSE protocol.

**Name:**
Specifies the identifier of the tool. This value must match the tool name expected by the MCP server.

**Url:**
Defines the HTTP endpoint where the tool is hosted and listening for requests.

**ApiKey (optional):**
Provides authentication for the Plugin TestConsole when connecting to the tool. This is currently the only supported authentication mechanism.

### Using Tools via STDIO and npx

The second configuration entry, "Everything", defines a [demo tool](https://github.com/modelcontextprotocol/servers/tree/main/src/everything) that includes a variety of functions to showcase how tools can be implemented.

**Command:**
Specifies the name of the executable used to launch the tool.

**Arguments:**
A JSON-serialised object containing the arguments to be passed when running the tool. In this example, the configuration will result in launching the Everything tool using the following command:

~~~bash
npx @modelcontextprotocol/server-everything
~~~

# Call to action

Please feel free to use this library and extend it as you need.
