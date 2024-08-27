// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
import { IActionContext } from "@microsoft/vscode-azext-utils";
import vscode from "vscode";
import { LanguageClient } from "vscode-languageclient/node";
import { OutputChannelManager } from "../utils/OutputChannelManager";
import { findOrCreateActiveBicepFile } from "./findOrCreateActiveBicepFile";
import { Command } from "./types";

export class BuildCommand implements Command {
  public readonly id = "bicep.build";
  public constructor(
    private readonly client: LanguageClient,
    private readonly _outputChannelManager: OutputChannelManager,
  ) {}

  public async execute(context: IActionContext, documentUri?: vscode.Uri | undefined): Promise<void> {
    let a:object = this.client;
    let b: object= this._outputChannelManager;
    a = b;
    b = a;

    documentUri = await findOrCreateActiveBicepFile(
      context,
      documentUri,
      "Choose which Bicep file to build into an ARM template",
    );

    vscode.commands.executeCommand("editor.action.rename", [documentUri.toString(), new vscode.Position(0, 7)]) //works

    // try {
    //   const buildOutput: string = await this.client.sendRequest("workspace/executeCommand", {
    //     command: "build",
    //     arguments: [documentUri.fsPath],
    //   });
    //   this.outputChannelManager.appendToOutputChannel(buildOutput);
    // } catch (err) {
    //   this.client.error("Bicep build failed", parseError(err).message, true);
    // }
  }
}
